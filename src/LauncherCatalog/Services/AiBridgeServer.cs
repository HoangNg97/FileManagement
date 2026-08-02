using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LauncherCatalog.Services.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LauncherCatalog.Services;

/// <summary>
/// In-process local HTTP server (Kestrel) that bridges WorkSync and the Gmail Chrome extension.
/// It binds to loopback only, answers <c>chrome-extension://</c> callers via permissive CORS, and
/// requires a Bearer token on the two work endpoints. Requests are processed strictly one-at-a-time
/// through an <see cref="AiRequestQueue"/>, each bounded by a 60-second timeout. The actual AI work
/// is delegated to an <see cref="IAiBackend"/> so the backend can be swapped without changing the
/// transport. See WORKSYNC_INTEGRATION_REQUEST.md for the wire contract.
/// </summary>
public sealed class AiBridgeServer : IAsyncDisposable
{
    /// <summary>Default loopback port; configurable via settings.</summary>
    public const int DefaultPort = 48717;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Generous upper bound on prompt size; larger prompts are rejected with 413.</summary>
    private const int MaxPromptChars = 1_000_000;

    private readonly int _port;
    private readonly IAiBackend _backend;
    private readonly AiRequestQueue _queue = new();
    private volatile string _token;
    private WebApplication? _app;

    public AiBridgeServer(string token, IAiBackend backend, int port = DefaultPort)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _port = port;
    }

    public int ListeningPort => _port;
    public bool IsRunning => _app is not null;

    /// <summary>Replaces the accepted Bearer token; takes effect immediately for new requests.</summary>
    public void UpdateToken(string token) => _token = token ?? throw new ArgumentNullException(nameof(token));

    /// <summary>Generates a new random 256-bit token as an uppercase hex string.</summary>
    public static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });

        // The host process is a WPF desktop app; suppress the default console/debug logging.
        builder.Logging.ClearProviders();

        // Loopback only (binds 127.0.0.1 and ::1); never exposed to the network.
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(_port));

        var app = builder.Build();

        // CORS handled explicitly so the exact headers the extension expects are always present,
        // and preflight (OPTIONS) short-circuits with 204.
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["Access-Control-Allow-Origin"] = "*";
            headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
            headers["Access-Control-Max-Age"] = "86400";

            if (HttpMethods.IsOptions(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });

        // Bearer-token gate for the two work endpoints. /api/health stays open for discovery.
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var needsAuth = path.StartsWithSegments("/api/analyze") || path.StartsWithSegments("/api/draft");
            if (needsAuth && !IsBearerValid(context.Request.Headers.Authorization))
            {
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "unauthorized");
                return;
            }

            await next();
        });

        MapEndpoints(app);

        await app.StartAsync(cancellationToken);
        _app = app;
    }

    private void MapEndpoints(WebApplication app)
    {
        var version = typeof(AiBridgeServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        // Unauthenticated discovery endpoint so the extension can detect the bridge.
        app.MapGet("/api/health", () => Results.Json(new
        {
            ok = true,
            version,
            providers = _backend.Providers
        }));

        app.MapPost("/api/analyze", (HttpContext context) => HandleGenerateAsync(context, isDraft: false));
        app.MapPost("/api/draft", (HttpContext context) => HandleGenerateAsync(context, isDraft: true));
    }

    private async Task HandleGenerateAsync(HttpContext context, bool isDraft)
    {
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(context.RequestAborted);
        }

        var prompt = TryExtractPrompt(body, out var parseError);
        if (parseError is not null)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, parseError);
            return;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, "prompt is required");
            return;
        }

        if (prompt!.Length > MaxPromptChars)
        {
            await WriteErrorAsync(context, StatusCodes.Status413PayloadTooLarge, "prompt too large");
            return;
        }

        if (!_backend.IsReady)
        {
            await WriteErrorAsync(context, StatusCodes.Status503ServiceUnavailable, "ai backend not ready");
            return;
        }

        try
        {
            if (isDraft)
            {
                var result = await _queue.EnqueueAsync(
                    ct => _backend.DraftAsync(prompt, ct), RequestTimeout, context.RequestAborted);
                await WriteJsonAsync(context, StatusCodes.Status200OK, result);
            }
            else
            {
                var result = await _queue.EnqueueAsync(
                    ct => _backend.AnalyzeAsync(prompt, ct), RequestTimeout, context.RequestAborted);
                await WriteJsonAsync(context, StatusCodes.Status200OK, result);
            }
        }
        catch (TimeoutException)
        {
            await WriteErrorAsync(context, StatusCodes.Status504GatewayTimeout, "request timed out");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; nothing to write.
        }
        catch (AiBackendException ex)
        {
            AppLogger.WriteInfo($"AI bridge backend error: {ex.Message}");
            await WriteErrorAsync(context, StatusCodes.Status502BadGateway, "ai backend error");
        }
        catch (Exception ex)
        {
            AppLogger.WriteInfo($"AI bridge unexpected error: {ex.Message}");
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, "internal error");
        }
    }

    private static string? TryExtractPrompt(string body, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(body))
        {
            error = "request body is required";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "request body must be a JSON object";
                return null;
            }

            if (!document.RootElement.TryGetProperty("prompt", out var promptElement)
                || promptElement.ValueKind != JsonValueKind.String)
            {
                error = "prompt is required";
                return null;
            }

            return promptElement.GetString();
        }
        catch (JsonException)
        {
            error = "invalid json";
            return null;
        }
    }

    private bool IsBearerValid(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return false;
        }

        const string scheme = "Bearer ";
        if (!authorizationHeader.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var provided = authorizationHeader[scheme.Length..].Trim();
        if (provided.Length == 0)
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(_token);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static Task WriteJsonAsync(HttpContext context, int statusCode, object payload)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(payload, context.RequestAborted);
    }

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error = message }, context.RequestAborted);
    }

    public async Task StopAsync()
    {
        if (_app is null)
        {
            return;
        }

        try
        {
            await _app.StopAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown; the process is exiting.
        }

        await _app.DisposeAsync();
        _app = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _queue.Dispose();
    }
}

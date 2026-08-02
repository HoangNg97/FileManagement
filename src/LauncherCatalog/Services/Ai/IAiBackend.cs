using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services.Ai;

/// <summary>
/// Thrown by an <see cref="IAiBackend"/> when the upstream AI provider fails (bad gateway, 502).
/// Distinct from cancellation/timeout so the HTTP layer can map it to the right status code.
/// </summary>
public sealed class AiBackendException : Exception
{
    public AiBackendException(string message) : base(message)
    {
    }

    public AiBackendException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Abstraction over whatever actually produces analysis and drafts. Stage 1 ships a
/// <see cref="StubAiBackend"/>; a later stage swaps in a Playwright-driven Gemini backend behind
/// this same interface without touching the HTTP server or the request queue.
/// </summary>
public interface IAiBackend
{
    /// <summary>Provider identifiers advertised on <c>/api/health</c> (e.g. <c>gemini</c>).</summary>
    IReadOnlyList<string> Providers { get; }

    /// <summary>
    /// Whether the backend can currently serve requests. When <c>false</c> the HTTP layer
    /// returns 503 instead of enqueuing work.
    /// </summary>
    bool IsReady { get; }

    /// <summary>Analyzes an email prompt and returns a structured classification.</summary>
    Task<AiAnalyzeResult> AnalyzeAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>Drafts a reply for the supplied prompt.</summary>
    Task<AiDraftResult> DraftAsync(string prompt, CancellationToken cancellationToken);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LauncherCatalog.Models;
using Microsoft.Playwright;

namespace LauncherCatalog.Services.Ai;

/// <summary>
/// Real AI backend that drives the Gemini Web UI with Playwright instead of using a Gemini API key.
/// A single non-headless browser runs against a persistent profile (so the login survives restarts),
/// preferring the user's installed Edge/Chrome for a natural fingerprint and only falling back to a
/// one-time Chromium download. All browser access is serialized by <see cref="_gate"/>; the request
/// queue already guarantees one job at a time, and the interactive login takes the same lock.
///
/// Per request it opens a fresh conversation, pastes the opaque prompt, waits for generation to
/// settle, scrapes the final answer, and hands it to <see cref="GeminiOutputSanitizer"/> to recover
/// the JSON object. Parsing/scrape failures surface as <see cref="AiBackendException"/> (HTTP 502);
/// when not signed in the backend reports <see cref="IsReady"/> = <c>false</c> (HTTP 503).
/// </summary>
public sealed class GeminiWebDriver : IAiBackend, IAsyncDisposable
{
    private const string GeminiUrl = "https://gemini.google.com/app";

    /// <summary>
    /// How many prompts to send into a single Gemini conversation before starting a fresh chat.
    /// Reusing one chat keeps the sidebar from filling up with an entry per request; refreshing after
    /// this bound stops the conversation's accumulated context from slowing or skewing later answers.
    /// </summary>
    private const int MaxPromptsPerChat = 30;

    /// <summary>
    /// If the current conversation has been idle at least this long, the next request starts a fresh
    /// chat (and resets the prompt count) even when the prompt bound has not been reached, so a stale
    /// thread from a previous week isn't silently resumed.
    /// </summary>
    private static readonly TimeSpan ChatIdleReset = TimeSpan.FromDays(7);

    private static readonly string[] PromptEditors =
    {
        "div.ql-editor[contenteditable='true']",
        "rich-textarea div[contenteditable='true']",
        "div[contenteditable='true'][role='textbox']",
        "textarea[aria-label]"
    };

    private static readonly string[] SendButtons =
    {
        "button[aria-label='Send message']",
        "button.send-button",
        "button[aria-label*='Send']"
    };

    private static readonly string[] StopButtons =
    {
        "button[aria-label='Stop response']",
        "button[aria-label*='Stop']"
    };

    private static readonly string[] ResponseContainers =
    {
        "message-content .model-response-text",
        ".model-response-text",
        "message-content"
    };

    private static readonly string[] NewChatButtons =
    {
        "button[aria-label='New chat']",
        "[data-test-id='new-chat-button']",
        "button[aria-label*='New chat']"
    };

    private static readonly string[] SignedOutMarkers =
    {
        "a[href*='accounts.google.com']",
        "a[aria-label*='Sign in']"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly string _profileDir;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _generationSettleWindow = TimeSpan.FromSeconds(1.8);
    private readonly EntryRepository _settings = new();

    private IPlaywright? _playwright;
    private IBrowserContext? _context;
    private volatile bool _isSignedIn;
    private bool _browsersInstalled;
    private static bool _driverLocated;

    // Chat-reuse tracking: how many prompts the current conversation has handled, whether a
    // conversation is currently active, and when the last prompt was submitted. The last-request
    // time and prompt count are persisted (settings DB) so the reuse rules survive app restarts;
    // _persistenceLoaded guards the one-time load.
    private int _promptsInCurrentChat;
    private bool _hasCurrentChat;
    private DateTime _lastPromptUtc = DateTime.MinValue;
    private bool _persistenceLoaded;

    // In-app chat slot (used by the WorkSync chat panel, separate from the Gmail bridge slot).
    // Both slots share the single Playwright browser tab; the driver forces a Gemini "New chat"
    // when switching between slots so their conversations stay separate without opening a second
    // browser tab. State is in-memory only — a fresh app run always starts a new Gemini chat for
    // the WorkSync chat panel.
    private int _chatPromptsInCurrentChat;
    private bool _chatHasCurrentChat;
    private DateTime _chatLastPromptUtc = DateTime.MinValue;

    // Which slot last used the browser tab. Bridge and Chat both drive the same Gemini page, and
    // switching slots is treated as "start a new Gemini conversation" so their threads don't mix.
    private enum ConversationSlot { None, Bridge, Chat }
    private ConversationSlot _currentSlot = ConversationSlot.None;

    // Settings keys for the persisted chat-reuse state (stored in catalog.db's settings table).
    private const string LastRequestKey = "ai_gemini_last_request_utc";
    private const string PromptCountKey = "ai_gemini_prompt_count";

    public GeminiWebDriver(string? profileDir = null)
    {
        _profileDir = profileDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LauncherCatalog",
            "GeminiProfile");
    }

    public IReadOnlyList<string> Providers { get; } = new[] { "gemini" };

    /// <summary>True once an interactive login has confirmed the Gemini prompt box is reachable.</summary>
    public bool IsReady => _isSignedIn;

    /// <summary>Which browser was launched (<c>msedge</c>, <c>chrome</c>, or <c>chromium</c>).</summary>
    public string? LaunchedChannel { get; private set; }

    /// <summary>
    /// UTC time of the most recent prompt sent to Gemini, or <c>null</c> if none is known yet. The
    /// value is persisted in settings and restored by <see cref="EnsureStateLoadedAsync"/>, so it
    /// survives app restarts.
    /// </summary>
    public DateTime? LastRequestUtc => _lastPromptUtc == DateTime.MinValue ? null : _lastPromptUtc;

    /// <summary>Loads the persisted chat-reuse state (last-request time and prompt count) once.</summary>
    public Task EnsureStateLoadedAsync() => EnsurePersistenceLoadedAsync();

    /// <summary>Raised when the signed-in state changes so the manager can refresh the UI.</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised when an incoming request could not be served because the Gemini session is no longer
    /// signed in (i.e. Google silently signed the user out). Fires once per silent sign-out: while
    /// signed out <see cref="IsReady"/> is <c>false</c>, so further requests are rejected at the HTTP
    /// layer without reaching the driver, and the event only fires again after a fresh sign-in.
    /// </summary>
    public event EventHandler? SignInRequired;

    /// <summary>
    /// Opens the visible browser on Gemini and waits (up to a few minutes) for the user to finish
    /// signing in. The human handles any 2FA/CAPTCHA; WorkSync never sees credentials.
    /// </summary>
    public async Task LoginAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool signedIn;
            try
            {
                signedIn = await RunLoginOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (IsClosedError(ex))
            {
                // The previous browser window was closed; its context is stale. Force a fresh launch
                // and try the sign-in flow one more time.
                AppLogger.WriteInfo("Gemini login: stale browser detected; relaunching.");
                await ForceCloseContextAsync().ConfigureAwait(false);
                signedIn = await RunLoginOnceAsync(cancellationToken).ConfigureAwait(false);
            }

            SetSignedIn(signedIn);
            AppLogger.WriteInfo($"Gemini login finished; signed in: {signedIn}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> RunLoginOnceAsync(CancellationToken cancellationToken)
    {
        await EnsureContextAsync(cancellationToken).ConfigureAwait(false);
        var page = await GetPageAsync().ConfigureAwait(false);

        await page.GotoAsync(GeminiUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        }).ConfigureAwait(false);

        try
        {
            await page.BringToFrontAsync().ConfigureAwait(false);
        }
        catch
        {
            // Bringing the window forward is best-effort.
        }

        return await WaitForSignedInAsync(page, TimeSpan.FromMinutes(3), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>True when a Playwright error means the page/context/browser was already closed.</summary>
    private static bool IsClosedError(PlaywrightException ex) =>
        ex.Message.Contains("has been closed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Target closed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Browser closed", StringComparison.OrdinalIgnoreCase);

    /// <summary>Disposes and clears the current context so the next call relaunches a fresh browser.</summary>
    private async Task ForceCloseContextAsync()
    {
        var context = _context;
        _context = null;
        _hasCurrentChat = false;
        _promptsInCurrentChat = 0;
        _chatHasCurrentChat = false;
        _chatPromptsInCurrentChat = 0;
        _currentSlot = ConversationSlot.None;
        if (context is null)
        {
            return;
        }

        context.Close -= OnContextClosed;
        try
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Already gone; nothing to clean up.
        }
    }

    public async Task<AiAnalyzeResult> AnalyzeAsync(string prompt, CancellationToken cancellationToken)
    {
        var json = await RunPromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        return Deserialize<AiAnalyzeResult>(json);
    }

    public async Task<AiDraftResult> DraftAsync(string prompt, CancellationToken cancellationToken)
    {
        var json = await RunPromptAsync(prompt, cancellationToken).ConfigureAwait(false);
        return Deserialize<AiDraftResult>(json);
    }

    /// <summary>
    /// Free-form chat entry point used by the in-app chat panel (not the Gmail bridge). Sends
    /// <paramref name="prompt"/> to Gemini and returns the plain-text answer. Context data is
    /// stitched into <paramref name="prompt"/> upstream (potentially across multiple sequential
    /// calls the chat panel makes when the data is large), so no file attachment is ever needed.
    /// </summary>
    public async Task<string> ChatAsync(string prompt, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                return await RunChatOnceAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (IsClosedError(ex))
            {
                AppLogger.WriteInfo("Gemini chat: stale browser detected; relaunching.");
                await ForceCloseContextAsync().ConfigureAwait(false);
                return await RunChatOnceAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Resets the in-app chat slot so the next <see cref="ChatAsync"/> call starts a fresh Gemini
    /// conversation. Used by the chat panel's "New conversation" button.
    /// </summary>
    public async Task ResetChatAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _chatHasCurrentChat = false;
            _chatPromptsInCurrentChat = 0;
            if (_currentSlot == ConversationSlot.Chat)
            {
                _currentSlot = ConversationSlot.None;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> RunChatOnceAsync(string prompt, CancellationToken cancellationToken)
    {
        // Short correlation id printed in every log line for this request so a failure banner in
        // the chat panel (which shows the same id) can be traced back to matching log lines.
        var traceId = Guid.NewGuid().ToString("N")[..8];
        AppLogger.WriteInfo($"Gemini chat [{traceId}]: begin request (prompt {prompt.Length} chars).");

        IPage? page = null;
        try
        {
            await EnsureContextAsync(cancellationToken).ConfigureAwait(false);
            page = await GetPageAsync().ConfigureAwait(false);

            await PrepareChatConversationAsync(page, cancellationToken).ConfigureAwait(false);

            if (!await IsSignedInAsync(page).ConfigureAwait(false))
            {
                SetSignedIn(false);
                SignInRequired?.Invoke(this, EventArgs.Empty);
                throw new AiBackendException($"[{traceId}] Not signed in to Gemini.");
            }

            var editor = await FindFirstVisibleAsync(page, PromptEditors, cancellationToken).ConfigureAwait(false)
                ?? throw new AiBackendException($"[{traceId}] Could not find the Gemini prompt input (DOM selector may have changed).");

            await editor.ClickAsync().ConfigureAwait(false);
            await page.Keyboard.InsertTextAsync(prompt).ConfigureAwait(false);

            var (responseSelector, baselineCount) = await SnapshotResponsesAsync(page).ConfigureAwait(false);

            await SubmitAsync(page, cancellationToken).ConfigureAwait(false);

            var text = await WaitForResponseAsync(page, responseSelector, baselineCount, cancellationToken)
                .ConfigureAwait(false);

            _chatLastPromptUtc = DateTime.UtcNow;
            _chatPromptsInCurrentChat++;

            AppLogger.WriteInfo(
                $"Gemini chat [{traceId}]: succeeded, scraped {text.Length} chars. " +
                $"Preview: {Preview(text)}");
            return text;
        }
        catch (AiBackendException ex)
        {
            await LogPageStateAsync(page, traceId, ex.Message).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            AppLogger.WriteInfo($"Gemini chat [{traceId}]: cancelled/timed out.");
            throw;
        }
        catch (PlaywrightException ex) when (IsClosedError(ex))
        {
            AppLogger.WriteInfo($"Gemini chat [{traceId}]: browser closed mid-request.");
            throw;
        }
        catch (Exception ex)
        {
            await LogPageStateAsync(page, traceId, ex.Message).ConfigureAwait(false);
            // Preserve the underlying reason in the message so the chat panel can show it inline
            // instead of the generic "automation failed" fallback.
            throw new AiBackendException($"[{traceId}] {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    /// <summary>Trims scraped text to a short preview safe for a single log line.</summary>
    private static string Preview(string text)
    {
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 200 ? normalized : normalized[..200] + "…";
    }

    /// <summary>
    /// Records diagnostic breadcrumbs (URL, sign-in state, latest scraped response, page title) after
    /// a chat failure so <c>latest.log</c> shows what the driver actually saw when it gave up.
    /// </summary>
    private async Task LogPageStateAsync(IPage? page, string traceId, string reason)
    {
        try
        {
            if (page is null)
            {
                AppLogger.WriteInfo($"Gemini chat [{traceId}]: failed before browser page was available. Reason: {reason}");
                return;
            }

            var url = page.Url;
            var title = string.Empty;
            try { title = await page.TitleAsync().ConfigureAwait(false); } catch { /* best-effort */ }

            var scraped = string.Empty;
            try
            {
                foreach (var selector in ResponseContainers)
                {
                    var loc = page.Locator(selector).Last;
                    if (await loc.CountAsync().ConfigureAwait(false) > 0)
                    {
                        scraped = await loc.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 2_000 })
                            .ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch
            {
                // Best-effort; if we can't reach the DOM the URL alone is still useful.
            }

            AppLogger.WriteInfo(
                $"Gemini chat [{traceId}]: FAILED. Reason: {reason}. " +
                $"Signed in: {_isSignedIn}. URL: {url}. Title: {title}. " +
                $"Latest response on page: {(string.IsNullOrWhiteSpace(scraped) ? "(none)" : Preview(scraped))}");
        }
        catch (Exception logEx)
        {
            AppLogger.WriteInfo($"Gemini chat [{traceId}]: failed to capture page state ({logEx.Message}). Original reason: {reason}");
        }
    }

    private async Task PrepareChatConversationAsync(IPage page, CancellationToken cancellationToken)
    {
        if (!page.Url.Contains("gemini.google.com", StringComparison.OrdinalIgnoreCase))
        {
            await page.GotoAsync(GeminiUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            }).ConfigureAwait(false);
        }

        var idleTooLong = _chatLastPromptUtc != DateTime.MinValue
            && DateTime.UtcNow - _chatLastPromptUtc >= ChatIdleReset;
        // The chat slot must open a fresh Gemini conversation when: (a) the tab was last used by
        // the Gmail bridge (avoid mixing threads), (b) this is the chat slot's first turn of the
        // session, (c) the current chat has hit MaxPromptsPerChat, or (d) it's been idle > 7 days.
        var slotChanged = _currentSlot != ConversationSlot.Chat;
        var needNewChat = slotChanged
            || !_chatHasCurrentChat
            || _chatPromptsInCurrentChat >= MaxPromptsPerChat
            || idleTooLong;
        if (needNewChat)
        {
            var newChat = await FindFirstVisibleAsync(page, NewChatButtons, cancellationToken).ConfigureAwait(false);
            if (newChat is not null)
            {
                await ClickWithOverlayGuardAsync(page, newChat, cancellationToken).ConfigureAwait(false);
                await page.WaitForTimeoutAsync(400).ConfigureAwait(false);
            }

            _chatHasCurrentChat = true;
            _chatPromptsInCurrentChat = 0;
        }

        _currentSlot = ConversationSlot.Chat;
    }

    /// <summary>
    /// Clicks a Gemini element while tolerating Angular CDK overlays that intercept pointer events
    /// (a common failure mode on the "New chat" button when a menu backdrop lingers). Presses Escape
    /// to dismiss overlays first, then tries a normal click, then falls back to a DOM-level click
    /// that bypasses hit-testing, then a forced click as a last resort.
    /// </summary>
    private static async Task ClickWithOverlayGuardAsync(
        IPage page, ILocator locator, CancellationToken cancellationToken)
    {
        try { await page.Keyboard.PressAsync("Escape").ConfigureAwait(false); } catch { /* best effort */ }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await locator.ClickAsync(new LocatorClickOptions { Timeout = 5_000 }).ConfigureAwait(false);
            return;
        }
        catch (PlaywrightException)
        {
            // Overlay is still intercepting; fall through to the JS-click fallback.
        }

        try
        {
            await locator.EvaluateAsync("el => el.click()").ConfigureAwait(false);
            return;
        }
        catch (PlaywrightException)
        {
            // Last resort: force click skips the actionability checks entirely.
        }

        await locator.ClickAsync(new LocatorClickOptions { Force = true, Timeout = 5_000 })
            .ConfigureAwait(false);
    }

    private async Task<string> RunPromptAsync(string prompt, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                return await RunPromptOnceAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (IsClosedError(ex))
            {
                // The browser was closed between requests; relaunch once and retry.
                AppLogger.WriteInfo("Gemini request: stale browser detected; relaunching.");
                await ForceCloseContextAsync().ConfigureAwait(false);
                return await RunPromptOnceAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> RunPromptOnceAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureContextAsync(cancellationToken).ConfigureAwait(false);
            var page = await GetPageAsync().ConfigureAwait(false);

            await PrepareConversationAsync(page, cancellationToken).ConfigureAwait(false);

            if (!await IsSignedInAsync(page).ConfigureAwait(false))
            {
                SetSignedIn(false);
                SignInRequired?.Invoke(this, EventArgs.Empty);
                throw new AiBackendException("Not signed in to Gemini.");
            }

            var editor = await FindFirstVisibleAsync(page, PromptEditors, cancellationToken).ConfigureAwait(false)
                ?? throw new AiBackendException("Could not find the Gemini prompt input.");

            await editor.ClickAsync().ConfigureAwait(false);
            await page.Keyboard.InsertTextAsync(prompt).ConfigureAwait(false);

            // Record how many answers already exist so we can wait for a *new* one after submitting
            // (in a reused chat the previous answer is already present and would otherwise be scraped).
            var (responseSelector, baselineCount) = await SnapshotResponsesAsync(page).ConfigureAwait(false);

            await SubmitAsync(page, cancellationToken).ConfigureAwait(false);

            var text = await WaitForResponseAsync(page, responseSelector, baselineCount, cancellationToken)
                .ConfigureAwait(false);

            // Only once the answer has come back do we record this turn: stamp the last-request time
            // and increment the count, then persist both in a single save.
            _lastPromptUtc = DateTime.UtcNow;
            _promptsInCurrentChat++;
            await SaveChatStateAsync().ConfigureAwait(false);

            var json = GeminiOutputSanitizer.ExtractJson(text)
                ?? throw new AiBackendException("Gemini returned no parseable JSON object.");
            return json;
        }
        catch (AiBackendException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlaywrightException ex) when (IsClosedError(ex))
        {
            // Let RunPromptAsync handle the relaunch-and-retry.
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.WriteInfo($"Gemini driver request error: {ex.Message}");
            throw new AiBackendException("Gemini automation failed.", ex);
        }
    }

    private async Task PrepareConversationAsync(IPage page, CancellationToken cancellationToken)
    {
        await EnsurePersistenceLoadedAsync().ConfigureAwait(false);

        if (!page.Url.Contains("gemini.google.com", StringComparison.OrdinalIgnoreCase))
        {
            await page.GotoAsync(GeminiUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            }).ConfigureAwait(false);
        }

        // Reuse the active conversation so the sidebar doesn't grow one chat per request. Start a new
        // chat on the first request of a session, once the current thread has handled
        // MaxPromptsPerChat prompts (bounding accumulated context), or when the thread has been idle
        // for at least ChatIdleReset (a stale thread from a previous week shouldn't be resumed). The
        // idle/count check uses persisted state, so these rules also hold across app restarts.
        // Also start fresh when the tab was last driven by the in-app chat panel, so the bridge and
        // the chat panel never share a Gemini thread even though they share the same browser tab.
        var idleTooLong = _lastPromptUtc != DateTime.MinValue
            && DateTime.UtcNow - _lastPromptUtc >= ChatIdleReset;
        var slotChanged = _currentSlot != ConversationSlot.Bridge;
        var needNewChat = slotChanged
            || !_hasCurrentChat
            || _promptsInCurrentChat >= MaxPromptsPerChat
            || idleTooLong;
        if (needNewChat)
        {
            var newChat = await FindFirstVisibleAsync(page, NewChatButtons, cancellationToken).ConfigureAwait(false);
            if (newChat is not null)
            {
                await ClickWithOverlayGuardAsync(page, newChat, cancellationToken).ConfigureAwait(false);
                await page.WaitForTimeoutAsync(400).ConfigureAwait(false);
            }

            _hasCurrentChat = true;
            _promptsInCurrentChat = 0;
            await SaveChatStateAsync().ConfigureAwait(false);
        }

        _currentSlot = ConversationSlot.Bridge;
    }

    /// <summary>Loads the persisted last-request time and prompt count once per instance.</summary>
    private async Task EnsurePersistenceLoadedAsync()
    {
        if (_persistenceLoaded)
        {
            return;
        }

        _persistenceLoaded = true;
        try
        {
            var storedTime = await _settings.GetSettingValueAsync(LastRequestKey).ConfigureAwait(false);
            if (!DateTime.TryParse(storedTime, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
            {
                return;
            }

            _lastPromptUtc = parsed.ToUniversalTime();

            var storedCount = await _settings.GetSettingValueAsync(PromptCountKey).ConfigureAwait(false);
            if (int.TryParse(storedCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                _promptsInCurrentChat = Math.Max(0, count);
            }

            // Resume the reuse window across restarts: keep using the open conversation when the saved
            // thread is still fresh (under the prompt bound and not idle past ChatIdleReset).
            var idleTooLong = DateTime.UtcNow - _lastPromptUtc >= ChatIdleReset;
            if (!idleTooLong && _promptsInCurrentChat < MaxPromptsPerChat)
            {
                _hasCurrentChat = true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.WriteInfo($"Gemini driver: could not load chat state: {ex.Message}");
        }
    }

    /// <summary>Persists the current last-request time and prompt count so they survive restarts.</summary>
    private async Task SaveChatStateAsync()
    {
        try
        {
            var time = _lastPromptUtc == DateTime.MinValue
                ? string.Empty
                : _lastPromptUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            await _settings.SetSettingValueAsync(LastRequestKey, time).ConfigureAwait(false);
            await _settings.SetSettingValueAsync(
                PromptCountKey, _promptsInCurrentChat.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.WriteInfo($"Gemini driver: could not save chat state: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the response-container selector that currently matches (if any) and how many answers
    /// are already on the page, so the caller can wait for a brand-new answer after submitting.
    /// </summary>
    private static async Task<(string? Selector, int Count)> SnapshotResponsesAsync(IPage page)
    {
        foreach (var selector in ResponseContainers)
        {
            var count = await page.Locator(selector).CountAsync().ConfigureAwait(false);
            if (count > 0)
            {
                return (selector, count);
            }
        }

        return (null, 0);
    }

    private async Task SubmitAsync(IPage page, CancellationToken cancellationToken)
    {
        // Give the editor a moment to enable the send control after the text lands.
        await page.WaitForTimeoutAsync(200).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var send = await FindFirstVisibleAsync(page, SendButtons, cancellationToken).ConfigureAwait(false);
        if (send is not null)
        {
            await send.ClickAsync().ConfigureAwait(false);
            return;
        }

        await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);
    }

    private async Task<string> WaitForResponseAsync(
        IPage page, string? knownSelector, int baselineCount, CancellationToken cancellationToken)
    {
        var responseSelector = knownSelector
            ?? await ResolveResponseSelectorAsync(page, cancellationToken).ConfigureAwait(false)
            ?? throw new AiBackendException("Could not locate a Gemini response container.");

        var lastText = string.Empty;
        var stableSince = DateTime.MinValue;
        var sawText = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Wait for a response beyond those that existed before submitting (chat-reuse safe).
            var count = await page.Locator(responseSelector).CountAsync().ConfigureAwait(false);
            if (count <= baselineCount)
            {
                stableSince = DateTime.MinValue;
                lastText = string.Empty;
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var locator = page.Locator(responseSelector).Last;
            string current;
            try
            {
                current = await locator.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5_000 })
                    .ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                current = string.Empty;
            }

            var stopVisible = await IsAnyVisibleAsync(page, StopButtons, cancellationToken).ConfigureAwait(false);
            var hasText = !string.IsNullOrWhiteSpace(current);
            sawText |= hasText;

            if (hasText && current == lastText && !stopVisible)
            {
                if (stableSince == DateTime.MinValue)
                {
                    stableSince = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - stableSince >= _generationSettleWindow)
                {
                    return current;
                }
            }
            else
            {
                stableSince = DateTime.MinValue;
            }

            lastText = current;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return sawText
            ? lastText
            : throw new AiBackendException(
                "Gemini produced no response in time (request budget exceeded before any answer text appeared).");
    }

    private async Task<string?> ResolveResponseSelectorAsync(IPage page, CancellationToken cancellationToken)
    {
        // Wait for the first response container to materialize, then keep using that selector.
        for (var attempt = 0; attempt < 60 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            foreach (var selector in ResponseContainers)
            {
                if (await page.Locator(selector).CountAsync().ConfigureAwait(false) > 0)
                {
                    return selector;
                }
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<bool> WaitForSignedInAsync(IPage page, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (await IsSignedInAsync(page).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(1_000, cancellationToken).ConfigureAwait(false);
        }

        return await IsSignedInAsync(page).ConfigureAwait(false);
    }

    private async Task<bool> IsSignedInAsync(IPage page)
    {
        if (page.Url.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var marker in SignedOutMarkers)
        {
            try
            {
                if (await page.Locator(marker).First.IsVisibleAsync().ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (PlaywrightException)
            {
                // Marker not present; keep checking.
            }
        }

        foreach (var selector in PromptEditors)
        {
            try
            {
                if (await page.Locator(selector).First.IsVisibleAsync().ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (PlaywrightException)
            {
                // Try the next candidate selector.
            }
        }

        return false;
    }

    private static async Task<ILocator?> FindFirstVisibleAsync(
        IPage page, IReadOnlyList<string> selectors, CancellationToken cancellationToken)
    {
        foreach (var selector in selectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = page.Locator(selector).First;
            try
            {
                if (await locator.IsVisibleAsync().ConfigureAwait(false))
                {
                    return locator;
                }
            }
            catch (PlaywrightException)
            {
                // Selector did not match; continue.
            }
        }

        return null;
    }

    private static async Task<bool> IsAnyVisibleAsync(
        IPage page, IReadOnlyList<string> selectors, CancellationToken cancellationToken)
    {
        return await FindFirstVisibleAsync(page, selectors, cancellationToken).ConfigureAwait(false) is not null;
    }

    private async Task<IPage> GetPageAsync()
    {
        var context = _context ?? throw new AiBackendException("Browser context is not available.");
        return context.Pages.FirstOrDefault() ?? await context.NewPageAsync().ConfigureAwait(false);
    }

    private async Task EnsureContextAsync(CancellationToken cancellationToken)
    {
        if (_context is not null)
        {
            return;
        }

        EnsureDriverLocated();
        _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
        Directory.CreateDirectory(_profileDir);

        foreach (var channel in GetChannelPreference())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (channel is null)
                {
                    EnsureBundledChromium();
                }

                var context = await _playwright.Chromium
                    .LaunchPersistentContextAsync(_profileDir, BuildLaunchOptions(channel))
                    .ConfigureAwait(false);

                // When the user closes the browser window (or it crashes) Playwright fires Close.
                // Drop the stale reference and forget the signed-in state so the next login relaunches
                // instead of throwing "Target page, context or browser has been closed".
                context.Close += OnContextClosed;

                _context = context;
                LaunchedChannel = channel ?? "chromium";
                AppLogger.WriteInfo($"Gemini driver launched browser channel '{LaunchedChannel}'.");
                return;
            }
            catch (Exception ex)
            {
                AppLogger.WriteInfo($"Gemini driver: '{channel ?? "chromium"}' launch failed: {ex.Message}");
            }
        }

        throw new AiBackendException("Could not launch any browser for Gemini automation.");
    }

    private void OnContextClosed(object? sender, IBrowserContext e)
    {
        if (sender is IBrowserContext closed)
        {
            closed.Close -= OnContextClosed;
        }

        if (ReferenceEquals(_context, sender))
        {
            _context = null;
        }

        _hasCurrentChat = false;
        _promptsInCurrentChat = 0;
        _chatHasCurrentChat = false;
        _chatPromptsInCurrentChat = 0;
        _currentSlot = ConversationSlot.None;
        AppLogger.WriteInfo("Gemini driver: browser context closed; will relaunch on next use.");
        SetSignedIn(false);
    }

    /// <summary>
    /// Builds the ordered list of Chromium channels to try. The user's Windows default browser is
    /// honored first (when it is Chrome or Edge, both Chromium-based and drivable by Playwright), then
    /// the other Chromium channel, then <c>null</c> (a bundled Chromium download) as a last resort.
    /// Non-Chromium defaults such as Firefox cannot be driven through the Chromium channel, so we fall
    /// back to whichever Chromium browser is installed.
    /// </summary>
    private static IEnumerable<string?> GetChannelPreference()
    {
        var ordered = new List<string?>();
        var preferred = GetDefaultBrowserChannel();
        if (preferred is not null)
        {
            ordered.Add(preferred);
        }

        foreach (var channel in new[] { "chrome", "msedge" })
        {
            if (!ordered.Contains(channel))
            {
                ordered.Add(channel);
            }
        }

        ordered.Add(null); // bundled Chromium fallback
        return ordered;
    }

    /// <summary>
    /// Reads the current user's default browser association for <c>https</c> and maps it to a
    /// Playwright Chromium channel (<c>chrome</c>/<c>msedge</c>). Returns <c>null</c> when the default
    /// is a non-Chromium browser (e.g. Firefox) or cannot be determined.
    /// </summary>
    private static string? GetDefaultBrowserChannel()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var progId = key?.GetValue("ProgId") as string;
            if (string.IsNullOrEmpty(progId))
            {
                return null;
            }

            if (progId.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
            {
                return "chrome";
            }

            if (progId.Contains("MSEdge", StringComparison.OrdinalIgnoreCase) ||
                progId.Contains("Edge", StringComparison.OrdinalIgnoreCase))
            {
                return "msedge";
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLogger.WriteInfo($"Gemini driver: could not read default browser: {ex.Message}");
            return null;
        }
    }

    private static BrowserTypeLaunchPersistentContextOptions BuildLaunchOptions(string? channel) => new()
    {
        Headless = false,
        Channel = channel,
        // Drop the automation banner/flag that most bot detectors key on.
        IgnoreDefaultArgs = new[] { "--enable-automation" },
        ViewportSize = ViewportSize.NoViewport
    };

    private void EnsureBundledChromium()
    {
        if (_browsersInstalled)
        {
            return;
        }
        AppLogger.WriteInfo("Gemini driver: installing bundled Chromium (one-time).");
        var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (exitCode != 0)
        {
            throw new AiBackendException($"Playwright Chromium install failed (exit code {exitCode}).");
        }

        _browsersInstalled = true;
    }

    /// <summary>
    /// Makes sure the Playwright Node driver is available on disk and pins
    /// <c>PLAYWRIGHT_DRIVER_SEARCH_PATH</c> so resolution is deterministic. WorkSync embeds the whole
    /// driver as a compressed resource, so a single moved EXE still works: if a loose
    /// <c>.playwright</c> folder sits next to the executable it is used as-is (dev/publish folder),
    /// otherwise the embedded copy is self-extracted once into a per-version cache folder.
    /// </summary>
    private static void EnsureDriverLocated()
    {
        if (_driverLocated)
        {
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var driverRoot = HasDriver(baseDir) ? baseDir : ExtractEmbeddedDriver();

        Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", driverRoot);
        _driverLocated = true;
    }

    private static bool HasDriver(string root)
    {
        return File.Exists(Path.Combine(root, ".playwright", "node", "win32_x64", "node.exe"))
            && File.Exists(Path.Combine(root, ".playwright", "package", "cli.js"));
    }

    /// <summary>
    /// Extracts the embedded <c>.playwright</c> driver zip into
    /// <c>%LOCALAPPDATA%\LauncherCatalog\PlaywrightDriver\{version}</c> the first time it is needed and
    /// returns that folder (the search root that contains <c>.playwright</c>).
    /// </summary>
    private static string ExtractEmbeddedDriver()
    {
        var assembly = typeof(GeminiWebDriver).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "0";
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LauncherCatalog",
            "PlaywrightDriver",
            version);

        if (HasDriver(root))
        {
            return root;
        }

        using var stream = assembly.GetManifestResourceStream("WorkSync.playwright-driver.zip")
            ?? throw new AiBackendException(
                "The embedded Playwright driver was not found in this build. Rebuild WorkSync so the " +
                "driver resource is packaged into the executable.");

        var extractTo = Path.Combine(root, ".playwright");
        Directory.CreateDirectory(extractTo);

        AppLogger.WriteInfo($"Gemini driver: extracting embedded Playwright driver to '{extractTo}'.");
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            archive.ExtractToDirectory(extractTo, overwriteFiles: true);
        }

        if (!HasDriver(root))
        {
            throw new AiBackendException("Extracting the embedded Playwright driver did not produce node.exe.");
        }

        return root;
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new AiBackendException("Gemini returned an empty JSON object.");
        }
        catch (JsonException ex)
        {
            throw new AiBackendException("Gemini returned JSON that did not match the expected schema.", ex);
        }
    }

    private void SetSignedIn(bool value)
    {
        if (_isSignedIn == value)
        {
            return;
        }

        _isSignedIn = value;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_context is not null)
            {
                _context.Close -= OnContextClosed;
                await _context.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort teardown.
        }

        _context = null;
        _playwright?.Dispose();
        _playwright = null;
        _gate.Dispose();
    }
}

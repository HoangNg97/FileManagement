# AI Bridge (Stages 1-3 built — live Gemini automation)

Back to [INDEX.md](INDEX.md).

> [!NOTE]
> **Status: ALL THREE STAGES BUILT.** The HTTP contract, FIFO queue, bearer auth, CORS, the full
> error mapping, the **"AI Bridge" settings tab**, and the **Playwright-driven Gemini Web backend**
> (`GeminiWebDriver`) with the **output sanitizer** (`GeminiOutputSanitizer`) all exist. The backend
> reports `providers: ["gemini"]` and returns **503** until the user signs in via the **"Log in to
> Gemini"** button. `StubAiBackend` remains in the tree (unused) for offline testing.
>
> **Caveat:** Gemini Web automation depends on the live Gemini DOM. The selectors in `GeminiWebDriver`
> are best-effort and may need tuning if Google changes the page. Validate against a real signed-in
> session before relying on it.

## Purpose

A Chrome extension ("AI Gmail Assistant", repo `C:\Projects\GmailExt`) wants WorkSync to act as a
**local AI gateway**. The extension sends an opaque prompt string; WorkSync runs it against Gemini
and returns fixed-shape JSON. The extension stays "dumb" — all model handling lives in WorkSync.

Source spec (owned by the extension): `C:\Projects\GmailExt\docs\WORKSYNC_INTEGRATION_REQUEST.md`.

## Implementation status

- [x] Stage 1 — HTTP contract + FIFO queue + **stubbed** AI responses (schema-valid canned JSON).
- [x] Stage 2 — AI Bridge tab + settings (enable, port, token copy/regenerate, status, login button)
  + DPAPI-encrypted token at rest.
- [x] Stage 3 — Playwright Gemini Web driver + output sanitizer (real automation behind the queue).
- [x] Docs flipped from PLANNED to current once each stage lands.

### In-app chat slot (used by WorkSync's own chat panel)

Beside the HTTP bridge endpoints, `GeminiWebDriver` also exposes `ChatAsync(prompt, ct)` used by
the in-app **Ask Gemini** panel (title-bar chat button). Both the bridge and the chat panel drive
the **same Playwright browser tab** — no second browser or Gemini tab is opened. The driver tracks
which slot last used the page (`_currentSlot`); whenever a request comes in for the other slot it
clicks Gemini's **New chat** first so the two conversations never share a thread. The chat slot's
state is **in-memory only** (a fresh app run always starts a new Gemini chat for the chat slot;
the 30-prompt / 7-day-idle reuse rules still apply within a session).

Context data is passed **inline** as JSON inside the prompt text — no file upload, no
`SetInputFilesAsync`, no `input[type='file']` selector chasing. The chat panel builds the JSON
with `ChatContextExporter.BuildAsync(tab)` and then:

- If the JSON + question + preamble fit inside `SingleRequestBudget` (8 000 chars), everything
  goes in one Gemini turn.
- Otherwise the JSON is split at line boundaries into `ChunkBodyBudget`-sized (5 000 char) pieces.
  Parts 1..N-1 are sent as their own turns with instructions to acknowledge with "OK" and wait,
  and part N is sent with the actual question. All turns land inside the same Gemini conversation
  via the shared chat slot, so Gemini stitches them itself. The pending bubble updates with
  `Chat.SendingContext` = "Sending data (i/N)…" so progress is visible.

The response is returned as **raw prose** (no JSON sanitizer). `AiBridgeManager.ChatAsync` guards
the call with the same disabled/port-in-use/not-signed-in checks the HTTP endpoints use, throwing
`AiBackendException("bridge_disabled" | "port_in_use" | "not_signed_in")` so the chat panel can
show a localized inline error above the input and refuse to send. Every chat request stamps a
short **trace id** into `latest.log`; the same id is embedded in any error message so a failure
banner can be correlated with the exact log lines.

### What exists after Stage 3

- `GeminiWebDriver` (`IAiBackend`) — drives the **Gemini Web UI** with Playwright in a **non-headless**
  browser against a **persistent profile** under `%LOCALAPPDATA%\LauncherCatalog\GeminiProfile`.
  Launch preference: installed **Edge (`msedge`) → Chrome (`chrome`) → bundled Chromium** (one-time
  `playwright install chromium` only if neither is present). The `--enable-automation` flag is
  dropped to reduce bot-detection. All browser access is serialized by an internal lock (the FIFO
  queue already guarantees one request at a time; interactive login takes the same lock).
- **Login flow**: the tab's "Log in to Gemini" button opens the visible browser on Gemini and waits
  (up to ~3 min) for the user to finish signing in (2FA/CAPTCHA handled by the human). Sign-in is
  detected by the presence of the prompt box; the tab shows a green "Signed in to Gemini" indicator.
- **Per request**: **reuse the current conversation** (to avoid one sidebar chat per request), paste
  the opaque prompt (`Keyboard.InsertText`), submit (send button or Enter), wait for a **new** answer
  to appear and its generation to settle (the response text is stable for ~1.8 s and no *Stop* button
  is visible), then scrape the last `.model-response-text` block. A fresh chat is started on the
  first request of a session, once the current thread has handled `MaxPromptsPerChat` (30) prompts,
  **or when the thread has been idle for at least `ChatIdleReset` (7 days)** — in which case the
  prompt count resets to 0. To scrape the correct answer in a reused chat, the driver records the
  response count *before* submitting and waits for the count to increase before reading the last
  response. The **last-request time and prompt count are persisted** (settings keys
  `ai_gemini_last_request_utc` and `ai_gemini_prompt_count` in `catalog.db`) in a single save **once
  the answer comes back** (so the timestamp reflects completed requests only), and restored on
  startup (`GeminiWebDriver.EnsureStateLoadedAsync`, called from `AiBridgeManager.InitializeAsync`),
  so the 30-prompt bound and the 7-day idle reset hold **across app restarts**, not just within one
  session.
- `GeminiOutputSanitizer` — strips `<think>…</think>`, removes Markdown code fences, and extracts the
  outermost balanced `{ … }` (string-aware brace matching). The driver deserializes that into
  `AiAnalyzeResult`/`AiDraftResult`; any scrape/parse failure → `AiBackendException` → **502**.
- **Not signed in** → `IsReady == false` → **503** before any work is enqueued.
- **Silent sign-out detection**: while `IsReady` is `true`, a request that reaches the driver and
  finds no signed-in session flips `IsReady` to `false` and raises `GeminiWebDriver.SignInRequired`
  (re-exposed as `AiBridgeManager.SignInRequired`). Because subsequent requests then short-circuit at
  the HTTP layer (503), the event fires **once per silent sign-out** and only again after a fresh
  sign-in. The shell (`MainWindow`) handles it by showing a persistent Windows tray notification
  ("Gemini sign-in required") that stays in the notification center until dismissed; clicking it
  restores the window and selects the AI Bridge tab.

### What exists after Stage 2

- Everything from Stage 1 (see below), now managed by `AiBridgeManager` (a process-wide singleton)
  instead of being started inline by the window.
- **"AI Bridge" tab** (`AiBridgeView` + `AiBridgeViewModel`) with: an enable switch that actually
  starts/stops the listener, an editable port (Apply restarts the server on the new port), the
  endpoint URL with Copy, the bearer token with Show/Hide + Copy + **Regenerate** (regenerate
  invalidates the old token immediately via `AiBridgeServer.UpdateToken`), a live status banner
  (running / disabled / port-in-use / error), and a **"Log in to Gemini"** button with a sign-in
  indicator (functional since Stage 3).
- **Port-in-use handling**: a taken port is detected (a probe `TcpListener` bind) and surfaced as a
  non-blocking `PortInUse` status; the feature stays down until the port is changed or freed. The app
  never auto-picks a different port.
- **DPAPI token at rest**: the bearer token is stored encrypted (`DpapiProtector`, `CurrentUser`
  scope, `DPAPI:`-prefixed). Legacy Stage 1 plaintext tokens are transparently migrated on load.

### What exists after Stage 1

- `AiBridgeServer` (Kestrel, loopback `127.0.0.1:48717`) with `GET /api/health`, `POST /api/analyze`,
  `POST /api/draft`, explicit CORS (`*`, `OPTIONS → 204`, `Max-Age 86400`), and Bearer auth on the
  two work endpoints.
- `AiRequestQueue` — strict FIFO, one job at a time, 60 s per-request wall-clock budget → `504`.
- `IAiBackend` + `StubAiBackend` — the swap point for the real backend. `StubAiBackend` (now unused
  by default) advertises `providers: ["stub"]` and returns canned, schema-valid analyze/draft
  payloads; the live backend is `GeminiWebDriver` (`providers: ["gemini"]`).
- Token persisted in `settings.ai_bridge_token` (DPAPI-encrypted since Stage 2), port read from
  `settings.ai_bridge_port` (default `48717`). Lifecycle owned by `AiBridgeManager`: started on app
  load when enabled, disposed on close.
- Verified error paths: `400` (bad JSON / missing prompt), `401` (missing/wrong token), `413`
  (oversize prompt), `503` (backend not ready), `504` (timeout).

## Confirmed decisions

| Topic | Decision |
|-------|----------|
| AI backend | Drive the **Gemini Web UI** via browser automation (no Gemini API key). |
| Automation lib | **Playwright** (chosen over Selenium for anti-bot robustness). |
| Concurrency | **FIFO queue, one request at a time.** Later requests wait (do not run in parallel). This intentionally deviates from the spec's "two parallel requests both succeed". |
| Per-request timeout | **60 s** wall-clock (including queue wait) → `504` if exceeded. |
| CORS | `Access-Control-Allow-Origin: *`, `OPTIONS → 204`, `Max-Age: 86400` (per spec). |
| Auth | One **bearer token** per install (`Authorization: Bearer <token>`); Copy + Regenerate in UI. |
| Secrets | Bearer token stored **DPAPI-encrypted**; Gemini session via a persistent browser profile. |
| UI | Dedicated **"AI Bridge" tab**. |
| Default state | **Enabled by default.** |
| Port | **48717**, user-configurable and persisted. |

## Network surface

- Kestrel bound to **`127.0.0.1:48717` only** (loopback; never `0.0.0.0`).
- Stays listening for the whole app lifetime, including while minimized to tray.
- CORS preflight (`OPTIONS`) returns **204** with the headers above on every endpoint.

## Endpoints

All JSON, UTF-8.

| Method + path | Auth | Body | Success response |
|---------------|------|------|------------------|
| `GET /api/health` | none | — | `{ ok, version, providers: ["gemini"] }` |
| `POST /api/analyze` | Bearer | `{ "prompt": "..." }` | classification schema (below) |
| `POST /api/draft` | Bearer | `{ "prompt": "..." }` | draft schema (below) |

`prompt` is opaque — WorkSync may compress/trim the body to fit the model but must **not** change
the answer schema.

### `/api/analyze` response

```json
{
  "category": "action_required | fyi | sensitive | draft_created",
  "summary": "<=300 chars",
  "detected_email_language": "en | ja | other",
  "user_is_directly_addressed": true,
  "reply_options": [ { "id": "...", "label": "...", "intent": "..." } ],
  "confidence": 0.0,
  "reasoning": "one short sentence"
}
```

### `/api/draft` response

```json
{
  "draft_language": "en",
  "subject": "...",
  "body": "...",
  "tone": "professional",
  "notes_for_user": "optional"
}
```

## Error contract

Every non-2xx response is `application/json` shaped `{ "error": "<reason>" }`.

| Status | When |
|--------|------|
| 400 | Body not valid JSON, or `prompt` missing/empty |
| 401 | Missing/invalid bearer token (`{ "error": "unauthorized" }`) |
| 413 | Prompt exceeds WorkSync's configured limit |
| 429 | Rate limited (include `Retry-After`); v1 only maps an upstream 429, no self-imposed limit |
| 500 | Unexpected WorkSync bug |
| 502 | Gemini returned an error or unparseable output (after sanitization) |
| 503 | Bridge enabled but not logged in / backend not ready |
| 504 | Exceeded the 60 s internal timeout |

## Request flow

1. Validate bearer token + JSON body.
2. Enqueue the job (FIFO). The request awaits its turn; its 60 s budget counts queue wait.
3. When at the front: open a fresh Gemini conversation, paste the prompt, submit, wait for the
   streamed answer to complete, scrape the final text.
4. **Sanitize**: strip `<think>…</think>`, strip ```` ```json ```` fences, extract the outer `{…}`,
   validate against the expected schema. If still invalid → `502`.
5. Return `200` with clean JSON.

## Gemini Web automation (anti-bot strategy)

The goal is ordinary automation that behaves like a human, to avoid tripping Google's bot defenses:

- **Real, non-headless browser.** Headless is the biggest detection trigger; run a visible window
  (can be minimized). Prefer launching the **user's installed Chrome/Edge** (Playwright `channel`)
  over a bundled Chromium, so the fingerprint matches a normal browser.
- **Persistent profile.** `launchPersistentContext` against a user-data dir under
  `%LOCALAPPDATA%\LauncherCatalog` so the Gemini login survives restarts.
- **Manual one-time login.** An "Log in to Gemini" button opens the visible browser; the user signs
  in normally (2FA/CAPTCHA handled by the human). WorkSync never sees credentials.
- **Human-paced interaction.** Typing with small delays, clicking the real send control, waiting for
  the generation-complete signal instead of polling aggressively. Conversation is reused across
  requests (fresh chat every `MaxPromptsPerChat` prompts, or after `ChatIdleReset` idle) so the
  sidebar doesn't fill up.
- **Graceful failure.** Logged-out/not-ready → `503`; slow generation → `504`; scrape failure →
  `502`; never crash the app.

### Browser distribution (implemented)

WorkSync does **not bundle Chromium into the EXE**. `GeminiWebDriver.EnsureContextAsync` launches, in
order of preference: (1) the **user's Windows default browser** when it is Chrome or Edge (detected
from the `https` `UserChoice` `ProgId` in the registry and mapped to Playwright's `chrome`/`msedge`
`channel`); (2) the other Chromium channel that is installed; (3) if neither launches, a **one-time
`playwright install chromium`** into the local Playwright cache. Non-Chromium defaults (e.g. Firefox)
can't be driven through the Chromium channel, so WorkSync falls back to whichever Chromium browser is
present. Either way the browser uses a persistent profile and runs non-headless.

> [!IMPORTANT]
> **Packaging note (single, movable EXE).** The `Microsoft.Playwright` package ships a Node-based
> driver (the `.playwright` folder containing `node.exe` + the JS `package`). To keep WorkSync a
> single file you can copy anywhere, the build **compresses the Windows driver into a zip and embeds
> it as a resource** (`WorkSync.playwright-driver.zip`) via the MSBuild target **`EmbedPlaywrightDriver`**
> in `LauncherCatalog.csproj`. At runtime `GeminiWebDriver.EnsureDriverLocated()` prefers a loose
> `.playwright` folder next to the EXE if present (dev/publish folder), otherwise it **self-extracts
> the embedded copy once** into `%LOCALAPPDATA%\LauncherCatalog\PlaywrightDriver\{version}` and pins
> `PLAYWRIGHT_DRIVER_SEARCH_PATH` there. This means the published `WorkSync.exe` carries the driver
> inside itself — no loose files need to travel with it. The NuGet package bundles node binaries for
> every OS (~0.5 GB); the target stages only the Windows node + package so the embedded zip stays
> ~33 MB. If you change the Playwright version, delete `obj\**\playwright-driver.zip` so it
> regenerates. The published app still relies on an installed Edge/Chrome or a first-run Chromium
> download for the browser itself.

## New settings keys (`settings` table in `catalog.db`)

| Key | Default | Notes |
|-----|---------|-------|
| `ai_bridge_enabled` | `true` | feature on/off; honored — toggling the switch starts/stops the listener |
| `ai_bridge_port` | `48717` | configurable and editable from the AI Bridge tab (Apply restarts the server) |
| `ai_bridge_token` | generated | bearer token, stored **DPAPI-encrypted** (`DPAPI:` prefix); legacy plaintext is migrated on load |
| `ai_gemini_last_request_utc` | empty | ISO-8601 UTC time of the last Gemini request; drives the 7-day chat idle reset across restarts |
| `ai_gemini_prompt_count` | `0` | prompts sent into the current Gemini chat; drives the 30-prompt new-chat bound across restarts |

No Gemini API key is stored (web automation instead).

## Components

| Component | Role | Status |
|-----------|------|--------|
| `AiBridgeServer` (reshaped from `BrowserBridgeServer`) | Kestrel host, auth, CORS, endpoints, error mapping | **Built (Stage 1)** |
| `AiRequestQueue` | `Channel`-based FIFO, single in-flight job, 60 s timeout | **Built (Stage 1)** |
| `IAiBackend` / `StubAiBackend` | backend abstraction + canned responses (swap point; stub now unused) | **Built (Stage 1)** |
| `AiBridgeManager` | singleton owning the server + backend + settings (enable/port/token/login lifecycle) | **Built (Stage 2)** |
| `DpapiProtector` | DPAPI encrypt/decrypt of the token at rest | **Built (Stage 2)** |
| AI Bridge tab (`AiBridgeView` + `AiBridgeViewModel`) | enable, port, token Copy/Regenerate, login, live status | **Built (Stage 2)** |
| `GeminiWebDriver` (`IAiBackend` impl) | Playwright automation of the Gemini Web UI (login + per-request flow) | **Built (Stage 3)** |
| `GeminiOutputSanitizer` | strip think/fences, extract balanced JSON object | **Built (Stage 3)** |

## Known risks (acknowledged)

- Scraping the Gemini Web UI is brittle (DOM changes, session expiry) and needs ongoing selector
  maintenance.
- The bearer token is the only guard preventing other local processes from using the bridge; loopback
  binding + token is the agreed trust boundary for a single-user local tool.

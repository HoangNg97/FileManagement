# AI Bridge (GitHub Copilot SDK backend)

Back to [INDEX.md](INDEX.md).

> [!NOTE]
> **Status:** the HTTP contract, FIFO queue, bearer auth, CORS, error mapping, the **"AI Bridge"
> settings tab**, and the **GitHub Copilot backend** (`CopilotBackend`) exist. Requests run on the
> signed-in user's **GitHub Copilot subscription**; until someone signs in, `POST /api/analyze` and
> `POST /api/draft` return **503**. (The previous Gemini Web/Playwright backend and in-app chat were removed.)

## Purpose

A Chrome extension ("AI Gmail Assistant", repo `C:\Projects\GmailExt`) uses WorkSync as a **local AI
gateway**. The extension sends an opaque prompt string; WorkSync runs it against an AI provider and
returns fixed-shape JSON. The extension stays "dumb" - all model handling lives in WorkSync.

Source spec (owned by the extension): `C:\Projects\GmailExt\docs\WORKSYNC_INTEGRATION_REQUEST.md`.

## GitHub Copilot backend

- **Package:** `GitHub.Copilot.SDK` (bundles the Copilot runtime under `runtimes\<rid>\native`). The
  publish is a self-contained folder, so the runtime ships as normal files next to `app\WorkSync.exe`.
- **Sign-in (web, subscription-based):** the **GitHub Copilot account** card at the top of the AI Bridge
  tab runs GitHub's **OAuth device flow** (`POST github.com/login/device/code` -> user enters the code
  on the opened `https://github.com/login/device` page -> poll `login/oauth/access_token`). The account
  is verified with `CopilotClient.GetAuthStatusAsync`, then the token is stored DPAPI-encrypted.
  Sign out forgets it; users can revoke it on GitHub (Settings > Applications).
- **Client ID:** WorkSync's own GitHub OAuth App (`CopilotBackend.ClientId` = `Ov23liz31madITPh6lfW`,
  device flow enabled) is built in, so users need no setup. No client secret is used. GitHub errors
  (e.g. app deleted or device flow disabled) are reported as readable sign-in errors.
- **Per request:** `CopilotClientMode.Empty` client (token passed as `GitHubToken`,
  `UseLoggedInUser = false`, home `%LOCALAPPDATA%\LauncherCatalog\Copilot`), a fresh session with
  **`AvailableTools = []`** (email text is untrusted; the model can't run tools), a replaced system
  message telling the model to answer only with JSON and ignore instructions inside emails, and
  `SendAndWaitAsync<T>` structured output deserialized into `AiAnalyzeResult` / `AiDraftResult`. The
  session is deleted afterwards so email content does not stay in Copilot's on-disk session state.
- **Errors:** SDK/runtime failures -> `AiBackendException` (**502**); timeouts -> **504**.
- **Model:** not pinned; the Copilot runtime default is used.

To swap providers later, implement `IAiBackend` and replace `CopilotBackend` in `AiBridgeManager`.

## Confirmed decisions

| Topic | Decision |
|-------|----------|
| Concurrency | **FIFO queue, one request at a time.** Later requests wait. |
| Per-request timeout | **60 s** wall-clock (including queue wait) -> `504` if exceeded. |
| CORS | `Access-Control-Allow-Origin: *`, `OPTIONS -> 204`, `Max-Age: 86400`. |
| Auth | One **bearer token** per install (`Authorization: Bearer <token>`); Copy + Regenerate in UI. |
| Secrets | Bearer token stored **DPAPI-encrypted** (`CurrentUser`, `DPAPI:` prefix). |
| UI | Dedicated **"AI Bridge" tab**. |
| Default state | **Enabled by default.** |
| Port | **48717**, user-configurable and persisted. Port-in-use is surfaced, never auto-changed. |

## Network surface

- Kestrel bound to **`127.0.0.1:48717` only** (loopback; never `0.0.0.0`).
- Stays listening for the whole app lifetime, including while minimized to tray.
- CORS preflight (`OPTIONS`) returns **204** with the headers above on every endpoint.

## Endpoints

All JSON, UTF-8.

| Method + path | Auth | Body | Success response |
|---------------|------|------|------------------|
| `GET /api/health` | none | - | `{ ok, version, providers: ["github-copilot"] }` |
| `POST /api/analyze` | Bearer | `{ "prompt": "..." }` | classification schema (below) |
| `POST /api/draft` | Bearer | `{ "prompt": "..." }` | draft schema (below) |

`prompt` is opaque - WorkSync may compress/trim the body to fit the model but must **not** change
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
| 429 | Rate limited (include `Retry-After`); only maps an upstream 429 |
| 500 | Unexpected WorkSync bug |
| 502 | AI provider returned an error or unparseable output |
| 503 | No GitHub account is signed in for Copilot |
| 504 | Exceeded the 60 s internal timeout |

## Settings keys (`settings` table in `catalog.db`)

| Key | Default | Notes |
|-----|---------|-------|
| `ai_bridge_enabled` | `true` | feature on/off; toggling the switch starts/stops the listener |
| `ai_bridge_port` | `48717` | editable from the AI Bridge tab (Apply restarts the server) |
| `ai_bridge_token` | generated | bearer token, stored **DPAPI-encrypted**; legacy plaintext is migrated on load |
| `ai_copilot_token` | empty | GitHub OAuth token for Copilot, **DPAPI-encrypted**; empty when signed out |
| `ai_copilot_login` | empty | GitHub login of the signed-in account |

Older installs may still have unused `ai_gemini_last_request_utc` / `ai_gemini_prompt_count` rows;
they are ignored.

## Components

| Component | Role |
|-----------|------|
| `AiBridgeServer` | Kestrel host, auth, CORS, endpoints, error mapping |
| `AiRequestQueue` | `Channel`-based FIFO, single in-flight job, 60 s timeout |
| `IAiBackend` | backend abstraction (swap point) |
| `CopilotBackend` | GitHub Copilot SDK backend + device-flow sign-in + token storage |
| `AiBridgeManager` | singleton owning server + backend + settings (enable/port/token/sign-in lifecycle) |
| `DpapiProtector` | DPAPI encrypt/decrypt of secrets at rest |
| AI Bridge tab (`AiBridgeView` + `AiBridgeViewModel`) | Copilot account (top), enable, port, token Copy/Regenerate, live status |

## Known risks (acknowledged)

- The bearer token is the only guard preventing other local processes from using the bridge; loopback
  binding + token is the agreed trust boundary for a single-user local tool.
- Every bridge request consumes the signed-in user's Copilot request allowance.
- Structured output (`SendAndWaitAsync<T>`) is marked experimental in the SDK (`GHCP001`).

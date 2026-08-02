using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LauncherCatalog.Models;

/// <summary>
/// One suggested reply the user can pick from in the Chrome extension.
/// </summary>
public sealed record AiReplyOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("intent")] string Intent);

/// <summary>
/// Result of <c>POST /api/analyze</c>. Property names are emitted in the snake_case shape the
/// Chrome extension expects (see WORKSYNC_INTEGRATION_REQUEST.md).
/// </summary>
public sealed record AiAnalyzeResult(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("detected_email_language")] string DetectedEmailLanguage,
    [property: JsonPropertyName("user_is_directly_addressed")] bool UserIsDirectlyAddressed,
    [property: JsonPropertyName("reply_options")] IReadOnlyList<AiReplyOption> ReplyOptions,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("reasoning")] string Reasoning);

/// <summary>
/// Result of <c>POST /api/draft</c>. Property names match the extension contract.
/// </summary>
public sealed record AiDraftResult(
    [property: JsonPropertyName("draft_language")] string DraftLanguage,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("tone")] string Tone,
    [property: JsonPropertyName("notes_for_user")] string NotesForUser);

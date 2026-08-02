using System;
using System.Text.RegularExpressions;

namespace LauncherCatalog.Services.Ai;

/// <summary>
/// Cleans the raw text scraped from the Gemini Web UI into a single JSON object string. The model
/// frequently wraps its answer in reasoning tags, Markdown code fences, or surrounding prose; this
/// strips all of that and returns the outermost balanced <c>{ ... }</c> block. Returns <c>null</c>
/// when no plausible JSON object can be recovered, in which case the caller maps the failure to a
/// 502 (bad gateway / unparseable upstream output).
/// </summary>
public static partial class GeminiOutputSanitizer
{
    [GeneratedRegex(@"<think\b[^>]*>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlockRegex();

    /// <summary>
    /// Extracts a single JSON object from <paramref name="raw"/>, or <c>null</c> if none is found.
    /// </summary>
    public static string? ExtractJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw;

        // 1. Drop chain-of-thought blocks the model sometimes emits.
        text = ThinkBlockRegex().Replace(text, string.Empty);

        // 2. Strip Markdown code fences (```json ... ``` or ``` ... ```), keeping the inner content.
        text = StripCodeFences(text);

        // 3. Pull out the first balanced { ... } object, ignoring braces inside strings.
        var json = ExtractBalancedObject(text);
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }

    private static string StripCodeFences(string text)
    {
        var fenceIndex = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceIndex < 0)
        {
            return text;
        }

        // Remove the opening fence line (``` or ```json) and the matching closing fence.
        var afterFence = text.IndexOf('\n', fenceIndex);
        if (afterFence < 0)
        {
            return text;
        }

        var closing = text.IndexOf("```", afterFence, StringComparison.Ordinal);
        if (closing < 0)
        {
            return text[(afterFence + 1)..];
        }

        return text[(afterFence + 1)..closing];
    }

    private static string? ExtractBalancedObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }

                    break;
            }
        }

        return null;
    }
}

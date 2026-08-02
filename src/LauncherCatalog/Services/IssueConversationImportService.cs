using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LauncherCatalog.Services;

/// <summary>
/// A single message parsed from an imported conversation JSON file. <see cref="Date"/> is null when
/// the source omitted it (the caller then substitutes the import time).
/// </summary>
public sealed record ImportedMessage(string Author, DateTime? Date, string Content);

/// <summary>
/// Parses a WorkSync conversation JSON file into <see cref="ImportedMessage"/> records so a long
/// conversation can be imported into an issue instead of being typed in by hand.
///
/// Supported shapes (both accepted for flexibility):
/// <code>
/// { "messages": [ { "author": "Name", "date": "2026-07-01T14:30:00", "content": "text" } ] }
/// </code>
/// or a bare top-level array of the same message objects. <c>date</c> is optional (ISO-8601); when
/// present but unparseable an error is raised so the user notices. <c>content</c> is required.
/// </summary>
public sealed class IssueConversationImportService
{
    public IReadOnlyList<ImportedMessage> Parse(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected file could not be found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new FormatException("The file is empty.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        JsonElement messagesElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            messagesElement = root;
        }
        else if (root.ValueKind == JsonValueKind.Object
                 && root.TryGetProperty("messages", out var messages)
                 && messages.ValueKind == JsonValueKind.Array)
        {
            messagesElement = messages;
        }
        else
        {
            throw new FormatException("Expected a 'messages' array or a top-level array of messages.");
        }

        var result = new List<ImportedMessage>();
        var index = 0;

        foreach (var element in messagesElement.EnumerateArray())
        {
            index++;

            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"Message #{index} is not a JSON object.");
            }

            var content = ReadString(element, "content");
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new FormatException($"Message #{index} is missing 'content'.");
            }

            var author = ReadString(element, "author") ?? string.Empty;
            var date = ReadDate(element, index);

            result.Add(new ImportedMessage(author, date, content));
        }

        if (result.Count == 0)
        {
            throw new FormatException("The file contains no messages.");
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static DateTime? ReadDate(JsonElement element, int index)
    {
        if (!element.TryGetProperty("date", out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"Message #{index} has a non-text 'date'.");
        }

        var raw = value.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        throw new FormatException($"Message #{index} has an unrecognized 'date' value: '{raw}'.");
    }
}

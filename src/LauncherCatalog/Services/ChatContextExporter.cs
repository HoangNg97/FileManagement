using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using LauncherCatalog.Models;

namespace LauncherCatalog.Services;

/// <summary>
/// Serializes the contents of one main-tab DB into a JSON string that the chat panel sends inline
/// to Gemini. No files are produced — the payload is stitched into the prompt (short data) or
/// split across several messages (long data) by <c>ChatViewModel</c>.
/// </summary>
public sealed class ChatContextExporter
{
    private readonly EntryRepository _entryRepository;
    private readonly TaskRepository _taskRepository;
    private readonly NoteRepository _noteRepository;
    private readonly HistoryRepository _historyRepository;
    private readonly IssueRepository _issueRepository;

    // Human-readable, Unicode-safe (Japanese etc. stays as-is) JSON. Indented output puts each
    // record on its own line so char-based chunking still cuts on line boundaries.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ChatContextExporter(
        EntryRepository entryRepository,
        TaskRepository taskRepository,
        NoteRepository noteRepository,
        HistoryRepository historyRepository,
        IssueRepository issueRepository)
    {
        _entryRepository = entryRepository;
        _taskRepository = taskRepository;
        _noteRepository = noteRepository;
        _historyRepository = historyRepository;
        _issueRepository = issueRepository;
    }

    /// <summary>Serialized tab payload: total record count and the JSON body sent to Gemini.</summary>
    public sealed record ContextData(string Json, int RowCount);

    /// <summary>
    /// Builds the JSON payload for the given tab. Returns <c>null</c> when the tab is unsupported
    /// or has no rows to send.
    /// </summary>
    public async Task<ContextData?> BuildAsync(ChatContextTab tab)
    {
        return tab switch
        {
            ChatContextTab.Catalog => await BuildCatalogAsync().ConfigureAwait(false),
            ChatContextTab.Tasks => await BuildTasksAsync().ConfigureAwait(false),
            ChatContextTab.Note => await BuildNotesAsync().ConfigureAwait(false),
            ChatContextTab.HistoryManagement => await BuildHistoryAsync().ConfigureAwait(false),
            ChatContextTab.Issues => await BuildIssuesAsync().ConfigureAwait(false),
            _ => null
        };
    }

    private async Task<ContextData?> BuildCatalogAsync()
    {
        var entries = await _entryRepository.LoadAsync().ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return null;
        }

        var payload = new List<object>(entries.Count);
        foreach (var entry in entries)
        {
            var path = entry.Type == EntryType.App
                ? entry.ExePath ?? entry.FolderPath
                : entry.FolderPath;
            payload.Add(new
            {
                displayName = entry.DisplayName,
                type = entry.Type.ToString(),
                path,
                tags = entry.Tags ?? new List<string>(),
                note = entry.Note,
                isFavorite = entry.IsFavorite,
                isMissing = entry.IsMissing,
                dateAdded = FormatDate(entry.DateAdded),
                lastOpened = FormatDate(entry.LastOpened)
            });
        }

        return Serialize("catalog", payload, entries.Count);
    }

    private async Task<ContextData?> BuildTasksAsync()
    {
        var tasks = await _taskRepository.LoadAsync().ConfigureAwait(false);
        if (tasks.Count == 0)
        {
            return null;
        }

        var payload = new List<object>(tasks.Count);
        foreach (var task in tasks)
        {
            payload.Add(new
            {
                title = task.Title,
                description = task.Description,
                status = task.Status.ToString(),
                priority = task.Priority.ToString(),
                dueDate = FormatDate(task.DueDate),
                isCompleted = task.IsCompleted,
                completedAt = FormatDate(task.CompletedAt),
                parentTaskId = task.ParentTaskId,
                createdAt = FormatDate(task.CreatedAt),
                updatedAt = FormatDate(task.UpdatedAt)
            });
        }

        return Serialize("tasks", payload, tasks.Count);
    }

    private async Task<ContextData?> BuildNotesAsync()
    {
        var notes = await _noteRepository.LoadAsync().ConfigureAwait(false);
        if (notes.Count == 0)
        {
            return null;
        }

        var payload = new List<object>(notes.Count);
        foreach (var note in notes)
        {
            payload.Add(new
            {
                title = note.Title,
                tags = note.Tags ?? new List<string>(),
                isFavorite = note.IsFavorite,
                createdAt = FormatDate(note.CreatedAt),
                updatedAt = FormatDate(note.UpdatedAt),
                lastViewedAt = FormatDate(note.LastViewedAt),
                body = note.Body
            });
        }

        return Serialize("notes", payload, notes.Count);
    }

    private async Task<ContextData?> BuildHistoryAsync()
    {
        var files = await _historyRepository.LoadFilesAsync().ConfigureAwait(false);
        if (files.Count == 0)
        {
            return null;
        }

        var payload = new List<object>(files.Count);
        var rowCount = 0;
        foreach (var file in files)
        {
            var events = await _historyRepository.LoadEventsAsync(file.Id).ConfigureAwait(false);
            var eventList = new List<object>(events.Count);
            foreach (var evt in events)
            {
                eventList.Add(new
                {
                    date = FormatDate(evt.EventDate),
                    note = evt.Note,
                    createdAt = FormatDate(evt.CreatedAt),
                    updatedAt = FormatDate(evt.UpdatedAt)
                });
                rowCount++;
            }

            payload.Add(new
            {
                name = file.Name,
                tags = file.Tags ?? new List<string>(),
                isFavorite = file.IsFavorite,
                createdAt = FormatDate(file.CreatedAt),
                updatedAt = FormatDate(file.UpdatedAt),
                events = eventList
            });
            rowCount++;
        }

        return Serialize("historyFiles", payload, rowCount);
    }

    private async Task<ContextData?> BuildIssuesAsync()
    {
        var issues = await _issueRepository.LoadAsync().ConfigureAwait(false);
        if (issues.Count == 0)
        {
            return null;
        }

        var payload = new List<object>(issues.Count);
        var rowCount = 0;
        foreach (var issue in issues)
        {
            var messageList = new List<object>(issue.Messages.Count);
            foreach (var message in issue.Messages)
            {
                messageList.Add(new
                {
                    author = message.CreatedBy,
                    date = FormatDate(message.CreatedDate),
                    content = message.Content,
                    isEdited = message.IsEdited
                });
                rowCount++;
            }

            payload.Add(new
            {
                title = issue.Title,
                note = issue.Note,
                createdAt = FormatDate(issue.CreatedDate),
                updatedAt = FormatDate(issue.UpdatedDate),
                messages = messageList
            });
            rowCount++;
        }

        return Serialize("issues", payload, rowCount);
    }

    private static ContextData Serialize(string kind, IReadOnlyList<object> items, int rowCount)
    {
        var root = new { kind, count = items.Count, items };
        var json = JsonSerializer.Serialize(root, JsonOptions);
        return new ContextData(json, rowCount);
    }

    private static string? FormatDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

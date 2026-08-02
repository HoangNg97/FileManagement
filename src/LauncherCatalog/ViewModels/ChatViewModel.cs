using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using LauncherCatalog.Models;
using LauncherCatalog.Services;
using LauncherCatalog.Services.Ai;

namespace LauncherCatalog.ViewModels;

/// <summary>
/// Drives the in-app Gemini chat panel. Watches which of the five main tabs is currently active,
/// serializes that tab's DB rows to a JSON string, and calls <see cref="AiBridgeManager.ChatAsync"/>
/// to reach Gemini. When the JSON is short enough it rides along with the question in a single
/// request; otherwise it is split across several sequential messages inside the same Gemini
/// conversation so the model still sees the full data before it answers. If the AI Bridge is not
/// usable the error is surfaced above the input field and nothing is sent.
/// </summary>
public sealed class ChatViewModel : ObservableObject
{
    // Single-request budget: total prompt (context + preamble + question) can't exceed this many
    // characters, otherwise we switch to the chunked flow. Kept well under Gemini's per-turn input
    // limit so we don't flirt with truncation.
    private const int SingleRequestBudget = 8_000;

    // Body budget per chunked message. Leaves headroom for the wrapper text ("Part i of N ...").
    private const int ChunkBodyBudget = 5_000;

    private readonly AiBridgeManager _bridge;
    private readonly ChatContextExporter _exporter;
    private readonly LocalizationService _localizer;
    private readonly Func<ChatContextTab> _activeTabProvider;

    private ChatContextTab _lastAttachedTab = ChatContextTab.Unsupported;
    private ChatContextTab _currentTab = ChatContextTab.Unsupported;
    private string _inputText = string.Empty;
    private string? _errorText;
    private bool _isSending;
    private string _tabDisplayName = string.Empty;
    private string _inputPlaceholder = string.Empty;
    private string _emptyStateText = string.Empty;
    private string _headerTitle = string.Empty;

    public ChatViewModel(
        AiBridgeManager bridge,
        ChatContextExporter exporter,
        Func<ChatContextTab> activeTabProvider)
    {
        _bridge = bridge;
        _exporter = exporter;
        _activeTabProvider = activeTabProvider;
        _localizer = LocalizationService.Current;

        Messages = new ObservableCollection<ChatMessage>();
        SendCommand = new AsyncRelayCommand(SendAsync, () => !IsSending);
        NewConversationCommand = new AsyncRelayCommand(NewConversationAsync);

        _localizer.CultureChanged += (_, _) => RefreshLocalized();
        RefreshLocalized();
    }

    public ObservableCollection<ChatMessage> Messages { get; }

    public ICommand SendCommand { get; }
    public ICommand NewConversationCommand { get; }

    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value ?? string.Empty);
    }

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorText);

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (SetProperty(ref _isSending, value))
            {
                (SendCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string TabDisplayName
    {
        get => _tabDisplayName;
        private set => SetProperty(ref _tabDisplayName, value);
    }

    public string InputPlaceholder
    {
        get => _inputPlaceholder;
        private set => SetProperty(ref _inputPlaceholder, value);
    }

    public string EmptyStateText
    {
        get => _emptyStateText;
        private set => SetProperty(ref _emptyStateText, value);
    }

    public string HeaderTitle
    {
        get => _headerTitle;
        private set => SetProperty(ref _headerTitle, value);
    }

    public bool IsEmpty => Messages.Count == 0;

    /// <summary>
    /// Called by the view whenever the panel opens or the user switches tabs so labels reflect the
    /// current context. Recomputes localized strings and clears any stale error banner.
    /// </summary>
    public void RefreshActiveTab()
    {
        _currentTab = _activeTabProvider();
        RefreshLocalized();
        ErrorText = null;
    }

    private void RefreshLocalized()
    {
        var tabName = _localizer.Get(TabResourceKey(_currentTab));
        TabDisplayName = tabName;
        HeaderTitle = _localizer.Format("Chat.Header", tabName);
        InputPlaceholder = _currentTab == ChatContextTab.Unsupported
            ? _localizer.Get("Chat.UnsupportedTab")
            : _localizer.Format("Chat.InputPlaceholder", tabName);
        EmptyStateText = _localizer.Format("Chat.EmptyState", tabName);
    }

    private static string TabResourceKey(ChatContextTab tab) => tab switch
    {
        ChatContextTab.Catalog => "Main.Tab.Catalog",
        ChatContextTab.Tasks => "Main.Tab.Tasks",
        ChatContextTab.Note => "Main.Tab.Note",
        ChatContextTab.HistoryManagement => "Main.Tab.HistoryManagement",
        ChatContextTab.Issues => "Main.Tab.Issues",
        _ => "Chat.UnsupportedTab"
    };

    private async Task SendAsync()
    {
        var text = InputText.Trim();
        if (text.Length == 0 || IsSending)
        {
            return;
        }

        _currentTab = _activeTabProvider();
        if (_currentTab == ChatContextTab.Unsupported)
        {
            ErrorText = _localizer.Get("Chat.UnsupportedTab");
            return;
        }

        var precheck = CheckBridge();
        if (precheck is not null)
        {
            ErrorText = precheck;
            return;
        }

        ErrorText = null;
        Messages.Add(new ChatMessage(ChatMessage.ChatRole.User, text));
        OnPropertyChanged(nameof(IsEmpty));
        InputText = string.Empty;

        var pending = new ChatMessage(ChatMessage.ChatRole.Pending, _localizer.Get("Chat.Thinking"));
        Messages.Add(pending);

        IsSending = true;
        try
        {
            // (Re)send context whenever the tab has changed since the last message, so every
            // conversation always sees the freshest data for its focus tab.
            ChatContextExporter.ContextData? context = null;
            if (_currentTab != _lastAttachedTab)
            {
                context = await _exporter.BuildAsync(_currentTab).ConfigureAwait(true);
            }

            var contextJson = context?.Json;
            var rowCount = context?.RowCount ?? 0;
            var kindLabel = KindLabel(_currentTab);
            string answer;

            if (contextJson is null || SingleRequestBudget - text.Length - 400 >= contextJson.Length)
            {
                // Short data (or nothing new to attach): one shot with the whole payload inline.
                var prompt = BuildSinglePrompt(kindLabel, contextJson, rowCount, text);
                answer = await _bridge.ChatAsync(prompt, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                answer = await SendChunkedAsync(kindLabel, contextJson, rowCount, text, pending)
                    .ConfigureAwait(true);
            }

            if (context is not null)
            {
                _lastAttachedTab = _currentTab;
            }

            pending.Content = string.IsNullOrWhiteSpace(answer)
                ? _localizer.Get("Chat.EmptyReply")
                : answer.Trim();
            pending.IsPending = false;
            // Swap the pending marker to a normal assistant message so bindings using Role update.
            var index = Messages.IndexOf(pending);
            if (index >= 0)
            {
                Messages[index] = new ChatMessage(ChatMessage.ChatRole.Assistant, pending.Content);
            }
        }
        catch (AiBackendException ex)
        {
            Messages.Remove(pending);
            ErrorText = MapBackendError(ex.Message);
            _lastAttachedTab = ChatContextTab.Unsupported;
        }
        catch (Exception ex)
        {
            Messages.Remove(pending);
            ErrorText = _localizer.Format("Chat.Error.Failed", ex.Message);
            _lastAttachedTab = ChatContextTab.Unsupported;
        }
        finally
        {
            IsSending = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Splits <paramref name="contextJson"/> into character-bounded chunks (preferring line breaks
    /// so JSON records stay on one side of the cut), sends the intermediate chunks with an
    /// "acknowledge only" instruction so Gemini stores them without answering, then sends the
    /// final chunk together with the actual question and returns Gemini's real reply. All chunks
    /// go through the same chat slot, so they live inside a single Gemini conversation.
    /// </summary>
    private async Task<string> SendChunkedAsync(
        string kindLabel, string contextJson, int rowCount, string question, ChatMessage pending)
    {
        var chunks = SplitJson(contextJson, ChunkBodyBudget);
        var total = chunks.Count;

        for (var i = 0; i < total - 1; i++)
        {
            pending.Content = _localizer.Format("Chat.SendingContext", i + 1, total);
            var prompt = BuildChunkPrompt(kindLabel, rowCount, i + 1, total, chunks[i], isFinal: false, question: null);
            _ = await _bridge.ChatAsync(prompt, CancellationToken.None).ConfigureAwait(true);
        }

        pending.Content = _localizer.Get("Chat.Thinking");
        var finalPrompt = BuildChunkPrompt(kindLabel, rowCount, total, total, chunks[^1], isFinal: true, question: question);
        return await _bridge.ChatAsync(finalPrompt, CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Cuts <paramref name="text"/> into pieces no longer than <paramref name="maxLen"/> characters.
    /// Prefers a newline boundary near the end of each piece so serialized JSON records aren't split
    /// mid-record — Gemini still sees the pieces as one stream, but the boundaries stay tidy.
    /// </summary>
    private static List<string> SplitJson(string text, int maxLen)
    {
        var pieces = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= maxLen)
            {
                pieces.Add(text.Substring(start));
                break;
            }

            var end = start + maxLen;
            var lastBreak = text.LastIndexOf('\n', end - 1, Math.Min(maxLen, end - start));
            var cut = lastBreak > start ? lastBreak + 1 : end;
            pieces.Add(text.Substring(start, cut - start));
            start = cut;
        }
        return pieces;
    }

    private async Task NewConversationAsync()
    {
        Messages.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        ErrorText = null;
        _lastAttachedTab = ChatContextTab.Unsupported;
        try
        {
            await _bridge.ResetChatAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLogger.WriteInfo($"Chat: failed to reset Gemini conversation: {ex.Message}");
        }
    }

    private string? CheckBridge()
    {
        if (!_bridge.IsEnabled)
        {
            return _localizer.Get("Chat.Error.Disabled");
        }
        if (_bridge.Status == AiBridgeStatus.PortInUse)
        {
            return _localizer.Get("Chat.Error.PortInUse");
        }
        if (_bridge.Status == AiBridgeStatus.Error)
        {
            return _localizer.Format("Chat.Error.Failed", _bridge.LastError ?? string.Empty);
        }
        if (!_bridge.IsSignedIn)
        {
            return _localizer.Get("Chat.Error.NotSignedIn");
        }
        return null;
    }

    private string MapBackendError(string message) => message switch
    {
        "bridge_disabled" => _localizer.Get("Chat.Error.Disabled"),
        "port_in_use" => _localizer.Get("Chat.Error.PortInUse"),
        "not_signed_in" => _localizer.Get("Chat.Error.NotSignedIn"),
        _ => _localizer.Format("Chat.Error.Failed", message)
    };

    private static string KindLabel(ChatContextTab tab) => tab switch
    {
        ChatContextTab.Catalog => "Catalog entries (apps and folders the user saved)",
        ChatContextTab.Tasks => "Tasks (status, priority, due date, and subtask hierarchy)",
        ChatContextTab.Note => "Notes (title, tags, and body)",
        ChatContextTab.HistoryManagement => "History files and their events",
        ChatContextTab.Issues => "Issues and their message threads",
        _ => "Data"
    };

    private static string BuildSinglePrompt(string kindLabel, string? contextJson, int rowCount, string question)
    {
        var builder = new StringBuilder();
        builder.Append("You are the AI assistant embedded inside WorkSync, a Windows desktop app. ");
        builder.Append("Answer conversationally in plain text (no JSON, no code fences). Be concise ");
        builder.Append("and reference specific records when useful. If the answer requires data that ");
        builder.Append("isn't in the payload below, say so.");
        builder.AppendLine();
        if (contextJson is not null)
        {
            builder.AppendLine();
            builder.Append("Context: the user's ").Append(kindLabel).Append(" (")
                .Append(rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(" record(s)) as JSON:");
            builder.AppendLine("```json");
            builder.AppendLine(contextJson);
            builder.AppendLine("```");
        }
        builder.AppendLine();
        builder.Append("User question: ").Append(question);
        return builder.ToString();
    }

    private static string BuildChunkPrompt(
        string kindLabel, int rowCount, int index, int total, string chunk, bool isFinal, string? question)
    {
        var builder = new StringBuilder();
        if (!isFinal)
        {
            builder.Append("You are the AI assistant embedded inside WorkSync. The user's ")
                .Append(kindLabel).Append(" is too large to send in one message, so it will arrive ")
                .Append("as JSON split across ").Append(total).Append(" parts. This is part ")
                .Append(index).Append(" of ").Append(total).Append(" (")
                .Append(rowCount.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .AppendLine(" total record(s)).");
            builder.AppendLine("Store this chunk. Do not answer any question yet. Reply only with 'OK' and wait for the next part.");
            builder.AppendLine();
            builder.AppendLine("```json");
            builder.Append(chunk);
            if (!chunk.EndsWith('\n')) builder.AppendLine();
            builder.AppendLine("```");
        }
        else
        {
            builder.Append("This is part ").Append(index).Append(" of ").Append(total)
                .AppendLine(" — the final chunk. Concatenate all previous chunks with this one to reconstruct the full JSON payload, then use it to answer the question below.");
            builder.AppendLine();
            builder.AppendLine("```json");
            builder.Append(chunk);
            if (!chunk.EndsWith('\n')) builder.AppendLine();
            builder.AppendLine("```");
            builder.AppendLine();
            builder.Append("Answer conversationally in plain text (no JSON, no code fences). Be concise ");
            builder.Append("and reference specific records when useful. If the answer requires data that ");
            builder.AppendLine("isn't in the reconstructed payload, say so.");
            builder.AppendLine();
            builder.Append("User question: ").Append(question);
        }
        return builder.ToString();
    }
}

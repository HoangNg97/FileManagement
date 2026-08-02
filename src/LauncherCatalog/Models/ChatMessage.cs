using System;
using LauncherCatalog.ViewModels;

namespace LauncherCatalog.Models;

/// <summary>A single bubble in the in-app chat panel. Content is plain text (URLs render as links).</summary>
public sealed class ChatMessage : ObservableObject
{
    public enum ChatRole
    {
        User,
        Assistant,
        Pending,
        Error
    }

    private string _content = string.Empty;
    private bool _isPending;

    public ChatMessage(ChatRole role, string content)
    {
        Role = role;
        _content = content;
        Timestamp = DateTime.Now;
        _isPending = role == ChatRole.Pending;
    }

    public ChatRole Role { get; }

    public DateTime Timestamp { get; }

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    /// <summary>Convenience flag: true while the assistant answer is still being produced.</summary>
    public bool IsPending
    {
        get => _isPending;
        set => SetProperty(ref _isPending, value);
    }

    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant || Role == ChatRole.Pending;
    public bool IsError => Role == ChatRole.Error;
}

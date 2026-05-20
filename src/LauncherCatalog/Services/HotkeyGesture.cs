using System.Windows.Input;

namespace LauncherCatalog.Services;

public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        return BuildText(
            "Ctrl",
            "Alt",
            "Shift",
            "Win",
            KeyToInvariantText);
    }

    public string ToDisplayString()
    {
        var localizer = LocalizationService.Current;
        return BuildText(
            localizer.Get("Hotkey.Modifier.Ctrl"),
            localizer.Get("Hotkey.Modifier.Alt"),
            localizer.Get("Hotkey.Modifier.Shift"),
            localizer.Get("Hotkey.Modifier.Win"),
            KeyToDisplayText);
    }

    public static string FormatForDisplay(string? value)
    {
        if (TryParse(value, out var gesture))
        {
            return gesture.ToDisplayString();
        }

        return value ?? string.Empty;
    }

    private string BuildText(
        string ctrlText,
        string altText,
        string shiftText,
        string winText,
        Func<Key, string> keyFormatter)
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add(ctrlText);
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add(altText);
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add(shiftText);
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add(winText);
        }

        parts.Add(keyFormatter(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Shift, Key.F);

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key key = Key.None;

        foreach (var part in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
                || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
            }
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)
                     || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Windows;
            }
            else if (!TryParseKey(part, out key))
            {
                return false;
            }
        }

        if (modifiers == ModifierKeys.None || key == Key.None || IsModifierKey(key))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static HotkeyGesture? FromKeyEvent(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System
            ? e.SystemKey
            : e.Key == Key.ImeProcessed
                ? e.ImeProcessedKey
                : e.Key;

        if (Keyboard.Modifiers == ModifierKeys.None || key == Key.None || IsModifierKey(key))
        {
            return null;
        }

        return new HotkeyGesture(Keyboard.Modifiers, key);
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;
    }

    private static string KeyToInvariantText(Key key)
    {
        return key switch
        {
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.OemPlus => "Plus",
            Key.OemMinus => "Minus",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            _ => key.ToString()
        };
    }

    private static string KeyToDisplayText(Key key)
    {
        var localizer = LocalizationService.Current;
        return key switch
        {
            Key.Return => localizer.Get("Hotkey.Key.Enter"),
            Key.Escape => localizer.Get("Hotkey.Key.Esc"),
            Key.Space => localizer.Get("Hotkey.Key.Space"),
            Key.OemPlus => localizer.Get("Hotkey.Key.Plus"),
            Key.OemMinus => localizer.Get("Hotkey.Key.Minus"),
            Key.OemComma => localizer.Get("Hotkey.Key.Comma"),
            Key.OemPeriod => localizer.Get("Hotkey.Key.Period"),
            _ => key.ToString()
        };
    }

    private static bool TryParseKey(string value, out Key key)
    {
        key = value switch
        {
            "Enter" => Key.Return,
            "Esc" => Key.Escape,
            "Plus" => Key.OemPlus,
            "Minus" => Key.OemMinus,
            "Comma" => Key.OemComma,
            "Period" => Key.OemPeriod,
            _ => Key.None
        };

        return key != Key.None || Enum.TryParse(value, ignoreCase: true, out key);
    }
}

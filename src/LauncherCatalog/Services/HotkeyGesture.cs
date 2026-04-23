using System.Windows.Input;

namespace LauncherCatalog.Services;

public sealed record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>();

        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyToText(Key));
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

    private static string KeyToText(Key key)
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

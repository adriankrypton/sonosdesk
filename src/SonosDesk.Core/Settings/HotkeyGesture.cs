using System.Diagnostics.CodeAnalysis;

namespace SonosDesk.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key, int VirtualKey)
{
    private static readonly Dictionary<string, int> KeyToVirtualKey = BuildKeyTable();
    private static readonly Dictionary<int, string> VirtualKeyToKey = KeyToVirtualKey
        .GroupBy(p => p.Value)
        .ToDictionary(g => g.Key, g => g.First().Key);

    private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = HotkeyModifiers.Control,
        ["Control"] = HotkeyModifiers.Control,
        ["Strg"] = HotkeyModifiers.Control,
        ["Alt"] = HotkeyModifiers.Alt,
        ["Shift"] = HotkeyModifiers.Shift,
        ["Win"] = HotkeyModifiers.Windows,
        ["Windows"] = HotkeyModifiers.Windows,
    };

    private static readonly HashSet<int> StandaloneKeys =
    [
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3, // volume and media keys
        .. Enumerable.Range(0x7C, 12), // F13–F24
    ];

    public static IReadOnlyCollection<string> KnownKeys => KeyToVirtualKey.Keys;

    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            if (!ModifierNames.TryGetValue(part, out var modifier) || modifiers.HasFlag(modifier))
            {
                return false;
            }

            modifiers |= modifier;
        }

        var keyPart = parts[^1];
        var key = KeyToVirtualKey.Keys.FirstOrDefault(k => string.Equals(k, keyPart, StringComparison.OrdinalIgnoreCase));
        if (key is null)
        {
            return false;
        }

        var virtualKey = KeyToVirtualKey[key];
        if (modifiers == HotkeyModifiers.None && !StandaloneKeys.Contains(virtualKey))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key, virtualKey);
        return true;
    }

    public static HotkeyGesture? FromVirtualKey(HotkeyModifiers modifiers, int virtualKey)
    {
        if (!VirtualKeyToKey.TryGetValue(virtualKey, out var key))
        {
            return null;
        }

        return modifiers == HotkeyModifiers.None && !StandaloneKeys.Contains(virtualKey)
            ? null
            : new HotkeyGesture(modifiers, key, virtualKey);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key);
        return string.Join('+', parts);
    }

    private static Dictionary<string, int> BuildKeyTable()
    {
        var table = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            table[c.ToString()] = c;
        }

        for (var d = 0; d <= 9; d++)
        {
            table[d.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 0x30 + d;
            table[$"NumPad{d}"] = 0x60 + d;
        }

        for (var f = 1; f <= 24; f++)
        {
            table[$"F{f}"] = 0x6F + f;
        }

        table["Space"] = 0x20;
        table["PageUp"] = 0x21;
        table["PageDown"] = 0x22;
        table["End"] = 0x23;
        table["Home"] = 0x24;
        table["Left"] = 0x25;
        table["Up"] = 0x26;
        table["Right"] = 0x27;
        table["Down"] = 0x28;
        table["Insert"] = 0x2D;
        table["Delete"] = 0x2E;
        table["Pause"] = 0x13;
        table["Multiply"] = 0x6A;
        table["Add"] = 0x6B;
        table["Subtract"] = 0x6D;
        table["Divide"] = 0x6F;
        table["Plus"] = 0xBB;
        table["Comma"] = 0xBC;
        table["Minus"] = 0xBD;
        table["Period"] = 0xBE;
        table["VolumeMute"] = 0xAD;
        table["VolumeDown"] = 0xAE;
        table["VolumeUp"] = 0xAF;
        table["MediaNext"] = 0xB0;
        table["MediaPrevious"] = 0xB1;
        table["MediaStop"] = 0xB2;
        table["MediaPlayPause"] = 0xB3;
        return table;
    }
}

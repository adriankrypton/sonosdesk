using System;
using System.Collections.Generic;

namespace SonosDesk.Windows.Tray;

public sealed record TrayMenuItem
{
    public static TrayMenuItem Separator { get; } = new() { IsSeparator = true };

    public string Text { get; init; } = string.Empty;

    public Action? Execute { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsChecked { get; init; }

    public bool IsHeader { get; init; }

    public bool IsSeparator { get; init; }

    public IReadOnlyList<TrayMenuItem>? Children { get; init; }

    public static TrayMenuItem Header(string text) => new() { Text = text, IsHeader = true, IsEnabled = false };

    public static TrayMenuItem Action(string text, Action execute, bool enabled = true) =>
        new() { Text = text, Execute = execute, IsEnabled = enabled };
}

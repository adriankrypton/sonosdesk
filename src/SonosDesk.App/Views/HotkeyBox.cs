using System.Windows.Controls;
using System.Windows.Input;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Settings;

namespace SonosDesk.App.Views;

public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ToolTip = L.T("app.hotkey_box_tooltip");
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        if (modifiers == HotkeyModifiers.None && key is Key.Back or Key.Delete or Key.Escape)
        {
            SetCurrentValue(TextProperty, string.Empty);
            return;
        }

        var gesture = HotkeyGesture.FromVirtualKey(modifiers, KeyInterop.VirtualKeyFromKey(key));
        if (gesture is not null)
        {
            SetCurrentValue(TextProperty, gesture.ToString());
        }
    }
}

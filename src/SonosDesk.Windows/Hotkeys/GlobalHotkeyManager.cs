using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Settings;
using SonosDesk.Windows.Interop;

namespace SonosDesk.Windows.Hotkeys;

public sealed record HotkeyRegistrationResult(string Name, HotkeyGesture Gesture, bool Success);

public sealed class GlobalHotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, string> _registered = [];
    private readonly ILogger<GlobalHotkeyManager> _logger;
    private int _nextId = 0x5D00;

    public GlobalHotkeyManager(ILogger<GlobalHotkeyManager> logger)
    {
        _logger = logger;
        _window = new MessageWindow(OnHotkey);
    }

    public event EventHandler<string>? HotkeyPressed;

    public HotkeyRegistrationResult Register(string name, HotkeyGesture gesture)
    {
        var id = _nextId++;
        var success = NativeMethods.RegisterHotKey(
            _window.Handle,
            id,
            (uint)gesture.Modifiers | NativeMethods.ModNoRepeat,
            (uint)gesture.VirtualKey);
        if (success)
        {
            _registered[id] = name;
        }
        else
        {
            _logger.LogInformation("Hotkey {Gesture} for {Name} is already in use", gesture, name);
        }

        return new HotkeyRegistrationResult(name, gesture, success);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys)
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }

        _registered.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.DestroyHandle();
    }

    private void OnHotkey(int id)
    {
        if (_registered.TryGetValue(id, out var name))
        {
            HotkeyPressed?.Invoke(this, name);
        }
    }

    private sealed class MessageWindow : NativeWindow
    {
        private static readonly IntPtr HwndMessage = new(-3);
        private readonly Action<int> _onHotkey;

        public MessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new CreateParams { Caption = "SonosDesk.Hotkeys", Parent = HwndMessage });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmHotkey)
            {
                _onHotkey(m.WParam.ToInt32());
                return;
            }

            base.WndProc(ref m);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Localization;

namespace SonosDesk.Windows.Tray;

public sealed class TrayIcon : IDisposable
{
    private const int MaxTooltipLength = 127;

    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _icon;
    private readonly ILogger<TrayIcon> _logger;
    private bool _disposed;

    public TrayIcon(ILogger<TrayIcon> logger)
    {
        _logger = logger;
        _icon = LoadIcon();
        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(2, 4, 2, 4),
        };
        _menu.Opening += OnMenuOpening;

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "SonosDesk",
            ContextMenuStrip = _menu,
            Visible = false,
        };
        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
    }

    public event EventHandler? Clicked;

    public event EventHandler? DoubleClicked;

    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public bool DarkMenu { get; set; }

    public void Show() => _notifyIcon.Visible = true;

    public void SetTooltip(string text)
    {
        if (_disposed)
        {
            return;
        }

        var value = string.IsNullOrWhiteSpace(text) ? "SonosDesk" : text.Replace('\n', ' ');
        _notifyIcon.Text = value.Length > MaxTooltipLength ? value[..(MaxTooltipLength - 1)] + "…" : value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.MouseDoubleClick -= OnMouseDoubleClick;
        _notifyIcon.Dispose();
        _menu.Opening -= OnMenuOpening;
        DisposeItems(_menu.Items);
        _menu.Dispose();
        _icon.Dispose();
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Clicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            DoubleClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        DisposeItems(_menu.Items);

        IReadOnlyList<TrayMenuItem> items;
        try
        {
            items = MenuProvider?.Invoke() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Building the tray menu failed");
            items = [TrayMenuItem.Header(L.T("win.tray.menu_failed"))];
        }

        // The provider may update DarkMenu, so the renderer is chosen afterwards.
        _menu.Renderer = new ThemedMenuRenderer(DarkMenu);
        AddItems(_menu.Items, items);
        e.Cancel = _menu.Items.Count == 0;
    }

    private void AddItems(ToolStripItemCollection target, IReadOnlyList<TrayMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                target.Add(new ToolStripSeparator());
                continue;
            }

            var menuItem = new ToolStripMenuItem(EscapeMnemonic(item.Text))
            {
                Enabled = item.IsEnabled || item.IsHeader,
                Checked = item.IsChecked,
            };

            if (item.IsHeader)
            {
                menuItem.Font = new Font(_menu.Font, FontStyle.Bold);
                menuItem.Tag = HeaderTag;
            }

            if (item.Children is { Count: > 0 } children)
            {
                AddItems(menuItem.DropDownItems, children);
                if (menuItem.DropDown is ToolStripDropDownMenu dropDown)
                {
                    dropDown.ShowImageMargin = false;
                    dropDown.ShowCheckMargin = true;
                    dropDown.Renderer = _menu.Renderer;
                }
            }
            else if (item.Execute is { } execute && item.IsEnabled && !item.IsHeader)
            {
                menuItem.Click += (_, _) => SafeExecute(execute, item.Text);
            }

            target.Add(menuItem);
        }
    }

    private void SafeExecute(Action execute, string name)
    {
        try
        {
            execute();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray menu action {Action} failed", name);
        }
    }

    private static readonly object HeaderTag = new();

    private static string EscapeMnemonic(string text) => text.Replace("&", "&&", StringComparison.Ordinal);

    private static void DisposeItems(ToolStripItemCollection items)
    {
        var copy = new ToolStripItem[items.Count];
        items.CopyTo(copy, 0);
        items.Clear();
        foreach (var item in copy)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                DisposeItems(menuItem.DropDownItems);
            }

            item.Dispose();
        }
    }

    private static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SonosDesk.Windows.SonosDesk.ico");
        return stream is null ? (Icon)SystemIcons.Application.Clone() : new Icon(stream, SystemInformation.SmallIconSize);
    }

    private sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly bool _dark;

        public ThemedMenuRenderer(bool dark)
            : base(new ThemedColorTable(dark))
        {
            _dark = dark;
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var header = e.Item.Tag == HeaderTag;
            e.TextColor = !e.Item.Enabled && !header
                ? (_dark ? Color.FromArgb(120, 120, 120) : Color.FromArgb(150, 150, 150))
                : (_dark ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28));
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = _dark ? Color.FromArgb(220, 220, 220) : Color.FromArgb(60, 60, 60);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            var r = e.ImageRectangle;
            using var pen = new Pen(_dark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 95, 184), 1.8f);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawLines(pen, [
                new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f),
            ]);
        }
    }

    private sealed class ThemedColorTable(bool dark) : ProfessionalColorTable
    {
        private readonly Color _background = dark ? Color.FromArgb(44, 44, 44) : Color.FromArgb(249, 249, 249);
        private readonly Color _hover = dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(234, 234, 234);
        private readonly Color _border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(220, 220, 220);

        public override Color ToolStripDropDownBackground => _background;

        public override Color ImageMarginGradientBegin => _background;

        public override Color ImageMarginGradientMiddle => _background;

        public override Color ImageMarginGradientEnd => _background;

        public override Color MenuBorder => _border;

        public override Color MenuItemBorder => _hover;

        public override Color MenuItemSelected => _hover;

        public override Color MenuItemSelectedGradientBegin => _hover;

        public override Color MenuItemSelectedGradientEnd => _hover;

        public override Color MenuItemPressedGradientBegin => _hover;

        public override Color MenuItemPressedGradientEnd => _hover;

        public override Color SeparatorDark => _border;

        public override Color SeparatorLight => _background;

        public override Color CheckBackground => _background;

        public override Color CheckSelectedBackground => _hover;

        public override Color CheckPressedBackground => _hover;
    }
}

using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Apollo.Services;

internal sealed class TrayIconService : IDisposable
{
    private readonly ToolStripMenuItem _monitoringItem;
    private readonly ToolStripMenuItem _recorderStatusItem;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon? _applicationIcon;
    private bool _disposed;

    public TrayIconService()
    {
        _monitoringItem = new ToolStripMenuItem("Turn monitoring on");
        _monitoringItem.Click += OnMonitoringClicked;

        _recorderStatusItem = new ToolStripMenuItem("Recorder: Idle")
        {
            Enabled = false,
        };

        var openItem = new ToolStripMenuItem("Open Apollo");
        openItem.Click += OnOpenClicked;

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += OnExitClicked;

        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_monitoringItem);
        menu.Items.Add(_recorderStatusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _applicationIcon = LoadApplicationIcon();
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _applicationIcon ?? SystemIcons.Application,
            Text = "Apollo",
            Visible = true,
        };
        _notifyIcon.DoubleClick += OnOpenClicked;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? MonitoringToggleRequested;

    public event EventHandler? ExitRequested;

    public void Update(bool monitoringAvailable, bool monitoringEnabled, string recorderStatus)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _monitoringItem.Enabled = monitoringAvailable;
        _monitoringItem.Checked = monitoringEnabled;
        _monitoringItem.Text = monitoringEnabled ? "Turn monitoring off" : "Turn monitoring on";
        _recorderStatusItem.Text = $"Recorder: {recorderStatus}";
        _notifyIcon.Text = monitoringEnabled ? "Apollo — Monitoring on" : "Apollo — Monitoring off";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.DoubleClick -= OnOpenClicked;
        _monitoringItem.Click -= OnMonitoringClicked;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _applicationIcon?.Dispose();
    }

    private static Icon? LoadApplicationIcon()
    {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            return Icon.ExtractAssociatedIcon(executablePath);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or ExternalException
                                          or FileNotFoundException)
        {
            return null;
        }
    }

    private void OnOpenClicked(object? sender, EventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnMonitoringClicked(object? sender, EventArgs e)
        => MonitoringToggleRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitClicked(object? sender, EventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}

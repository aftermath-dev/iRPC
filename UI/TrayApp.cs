using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace iRPC;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly IracingService _iracing = new();
    private readonly DiscordService _discord = new();
    private AppSettings _settings = AppSettings.Load();
    private readonly Queue<(DateTime Time, SessionData Data)> _pollBuffer = new();
    private readonly Dictionary<ConnState, Icon> _icons = new();
    private Icon? _plainIcon;
    private ConnState _connState = ConnState.Disconnected;
    private bool _presencePaused;

    private enum ConnState { Disconnected, IracingOnly, Full }

    public TrayApp()
    {
        var pauseItem = new ToolStripMenuItem("Pause Presence") { CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) =>
        {
            _presencePaused = pauseItem.Checked;
            if (_presencePaused) _discord.Clear();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, OnSettings);
        menu.Items.Add("Stats", null, OnStats);
        menu.Items.Add("Reconnect Discord", null, (_, _) => _discord.Reconnect());
        menu.Items.Add(pauseItem);
        menu.Items.Add("Check for Updates", null, OnCheckForUpdates);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);

        _trayIcon = new NotifyIcon
        {
            Text = "iRPC",
            Icon = GetIcon(ConnState.Disconnected),
            ContextMenuStrip = menu,
            Visible = true,
        };

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;
        _timer.Start();

        if (Logger.ConsumeCrashMarker())
        {
            _trayIcon.BalloonTipTitle = "iRPC";
            _trayIcon.BalloonTipText  = "iRPC closed unexpectedly last time. Check iRPC.log for details.";
            _trayIcon.BalloonTipIcon  = ToolTipIcon.Warning;
        }
        else
        {
            _trayIcon.BalloonTipTitle = "iRPC";
            _trayIcon.BalloonTipText  = "iRPC is running in the system tray.";
            _trayIcon.BalloonTipIcon  = ToolTipIcon.Info;
        }
        _trayIcon.ShowBalloonTip(3000);

        Logger.Enabled = _settings.DebugMode;
        TrackCollector.Enabled = _settings.TrackAndCarLogging;
        CarCollector.Enabled = _settings.TrackAndCarLogging;
        ApplyStartup(_settings.LaunchOnStartup);

        if (_settings.AutoPopulateKeyOverrides)
            KeyOverrides.SyncFromTracks();

        // Silent startup check — only notify if an update is available
        if (_settings.CheckForUpdatesOnStartup)
            _ = CheckForUpdatesAsync(silent: true);

        var pending = UpdateChecker.ConsumePendingReleaseNotes();
        if (pending is not null)
            MessageBox.Show(
                string.IsNullOrWhiteSpace(pending.Notes) ? "No release notes provided." : pending.Notes,
                $"iRPC {pending.Version} — What's New",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        SessionData data = _iracing.Poll();
        IRatingTracker.Record(data);
        data.IRatingAvg5 = IRatingTracker.AverageOfLast(5);
        data.IRatingAvg10 = IRatingTracker.AverageOfLast(10);
        data.IRatingAvgCustom = IRatingTracker.AverageOfLast(_settings.IRatingAvgCustomWindow);
        if (!_presencePaused)
            _discord.Update(data, _settings);
        StatsTracker.Record(data);
        _pollBuffer.Enqueue((DateTime.Now, data));
        if (_pollBuffer.Count > 5) _pollBuffer.Dequeue();

        var state = !data.IsConnected ? ConnState.Disconnected
            : _discord.IsConnected ? ConnState.Full
            : ConnState.IracingOnly;
        if (state != _connState)
        {
            _connState = state;
            _trayIcon.Icon = GetIcon(state);
        }
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        using var win = new SettingsWindow(_settings, newSettings =>
        {
            _settings = newSettings;
            Logger.Enabled = _settings.DebugMode;
            TrackCollector.Enabled = _settings.TrackAndCarLogging;
            CarCollector.Enabled = _settings.TrackAndCarLogging;
            ApplyStartup(_settings.LaunchOnStartup);
        }, () => _pollBuffer.ToList());
        win.Icon = GetPlainIcon();
        win.ShowDialog();
    }

    private void OnStats(object? sender, EventArgs e)
    {
        using var win = new StatsWindow(StatsTracker.Snapshot()) { Icon = GetPlainIcon() };
        win.ShowDialog();
    }

    private async void OnCheckForUpdates(object? sender, EventArgs e) =>
        await CheckForUpdatesAsync(silent: false);

    private async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            var result = await UpdateChecker.CheckAsync();
            if (result.HasUpdate)
            {
                var answer = MessageBox.Show(
                    $"v{result.LatestTag} is available (you have v{UpdateChecker.CurrentVersion.ToString(3)}).\n\nDownload and install it now? iRPC will restart.",
                    "iRPC Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (answer == DialogResult.Yes)
                {
                    if (result.AssetUrl is null)
                    {
                        MessageBox.Show(
                            "Couldn't find a downloadable installer for this release. Opening the download page instead.",
                            "iRPC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = result.ReleaseUrl,
                            UseShellExecute = true,
                        });
                        return;
                    }

                    using var progress = new UpdateProgressDialog(result.LatestTag.TrimStart('v'));
                    progress.Icon = GetPlainIcon();
                    progress.Show();

                    try
                    {
                        await UpdateChecker.DownloadAndPrepareRestartAsync(
                            result,
                            (received, total) => progress.ReportProgress(received, total),
                            progress.Cts.Token);
                        progress.Close();
                        UpdateChecker.SavePendingReleaseNotes(result.LatestTag, result.ReleaseNotes);
                        Shutdown();
                    }
                    catch (OperationCanceledException)
                    {
                        progress.Close();
                    }
                    catch (Exception ex)
                    {
                        progress.Close();
                        MessageBox.Show($"Update failed: {ex.Message}",
                            "iRPC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            else if (!silent)
            {
                MessageBox.Show(
                    $"You're up to date (v{UpdateChecker.CurrentVersion.ToString(3)}).",
                    "iRPC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch
        {
            if (!silent)
                MessageBox.Show("Couldn't reach GitHub. Check your connection and try again.",
                    "iRPC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnExit(object? sender, EventArgs e) => Shutdown();

    private void Shutdown()
    {
        _timer.Stop();
        StatsTracker.Flush();
        _discord.Dispose();
        _iracing.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private static void ApplyStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null) return;

        if (enable)
            key.SetValue("iRPC", Application.ExecutablePath);
        else
            key.DeleteValue("iRPC", throwOnMissingValue: false);
    }

    private Icon GetIcon(ConnState state)
    {
        if (_icons.TryGetValue(state, out var cached)) return cached;
        var icon = CreateIcon(state);
        _icons[state] = icon;
        return icon;
    }

    // Dialogs (Settings/Stats/Update progress) set their Icon once at construction and never
    // touch it again, so handing them the live connection-status icon leaves a stale colored dot
    // on the taskbar thumbnail for as long as the dialog stays open. Those windows aren't status
    // indicators, so give them the plain app icon instead — only the tray icon shows status.
    private Icon GetPlainIcon() => _plainIcon ??= CreatePlainIcon();

    private static Icon CreatePlainIcon()
    {
        var stream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("iRPC.ArtAssets.icon.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    private static Icon CreateIcon(ConnState state)
    {
        Bitmap bmp;
        var stream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("iRPC.ArtAssets.icon.ico");
        if (stream != null)
        {
            using var baseIcon = new Icon(stream);
            bmp = baseIcon.ToBitmap();
        }
        else
        {
            // Fall back to a plain green circle
            bmp = new Bitmap(16, 16);
            using var g0 = Graphics.FromImage(bmp);
            g0.Clear(Color.Transparent);
            g0.FillEllipse(new SolidBrush(Color.FromArgb(0, 168, 107)), 1, 1, 14, 14);
        }

        Color dot = state switch
        {
            ConnState.Full => Color.FromArgb(0, 200, 83),          // iRacing + Discord connected
            ConnState.IracingOnly => Color.FromArgb(250, 173, 20),  // iRacing only
            _ => Color.FromArgb(120, 120, 120),                     // disconnected
        };

        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int d = Math.Max(4, bmp.Width / 2);
        var rect = new Rectangle(bmp.Width - d, bmp.Height - d, d, d);
        g.FillEllipse(new SolidBrush(Color.Black), rect);
        rect.Inflate(-1, -1);
        g.FillEllipse(new SolidBrush(dot), rect);

        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StatsTracker.Flush();
            _trayIcon.Dispose();
            _timer.Dispose();
            _discord.Dispose();
            _iracing.Dispose();
            foreach (var icon in _icons.Values) icon.Dispose();
        }
        base.Dispose(disposing);
    }
}

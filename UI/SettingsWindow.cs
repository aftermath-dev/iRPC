using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace iRPC;

public class SettingsWindow : Form
{
    public AppSettings Settings { get; private set; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string app, string? idList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int v = 1;
        DwmSetWindowAttribute(Handle, 20, ref v, sizeof(int));
    }

    private static readonly Color BgForm    = Color.FromArgb(43, 45, 49);
    private static readonly Color BgInput   = Color.FromArgb(24, 25, 28);
    private static readonly Color BgAccent  = Color.FromArgb(88, 101, 242);
    private static readonly Color BgClose   = Color.FromArgb(64, 66, 73);
    private static readonly Color BgDivider = Color.FromArgb(60, 62, 68);
    private static readonly Color TextPrimary = Color.FromArgb(219, 222, 225);
    private static readonly Color TextMuted   = Color.FromArgb(148, 155, 164);
    private static readonly Color GreenSaved  = Color.FromArgb(87, 242, 135);

    private static readonly string[] SessionKeys =
        ["Practice", "Qualify", "Race", "Test Drive", "Time Trial"];

    private readonly Dictionary<string, SessionPresenceConfig> _templates;
    private string _currentSessionKey = "Practice";

    private readonly Label    _lblSaved;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private int _fadeStep;

    private readonly DarkDropDown _cmbSession;
    private readonly BrickRow     _brDetails;
    private readonly BrickRow     _brState;
    private readonly Label        _previewDetails;
    private readonly Label        _previewState;
    private readonly CheckBox     _cbElapsedTimer;
    private readonly DarkDropDown _cmbLargeIcon;
    private readonly BrickRow     _brLargeText;
    private readonly DarkDropDown _cmbSmallIcon;
    private readonly BrickRow     _brSmallText;
    private Panel? _scroll;
    private readonly TextBox  _appIdBox;
    private readonly CheckBox _cbLaunchOnStartup;
    private readonly CheckBox _cbShowGitHubButton;
    private readonly CheckBox _cbAutoPopulateKeyOverrides;
    private readonly CheckBox _cbDebugMode;
    private readonly CheckBox _cbTrackAndCarLogging;

    private readonly Action<AppSettings> _onSave;
    private readonly Func<List<(DateTime Time, SessionData Data)>> _getTelemetry;

    public SettingsWindow(AppSettings current, Action<AppSettings> onSave,
        Func<List<(DateTime Time, SessionData Data)>> getTelemetry)
    {
        _onSave       = onSave;
        _getTelemetry = getTelemetry;
        Settings      = current;

        _templates = current.SessionTemplates.ToDictionary(
            kv => kv.Key,
            kv => new SessionPresenceConfig
            {
                DetailsTemplate = kv.Value.DetailsTemplate,
                StateTemplate   = kv.Value.StateTemplate,
            });
        foreach (var key in SessionKeys)
            _templates.TryAdd(key, new SessionPresenceConfig());

        Text = "iRPC Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new Size(520, 710);
        BackColor       = BgForm;

        // ── Scrollable content area ──────────────────────────────
        _scroll = new Panel
        {
            Left = 0, Top = 0, Width = 520, Height = 666,
            AutoScroll = true, BackColor = BgForm,
        };
        Controls.Add(_scroll);
        var scroll = _scroll;
        Load += (_, _) => SetWindowTheme(scroll.Handle, "DarkMode_Explorer", null);

        const int x = 16;
        int y = 12;

        // ── Presence section ─────────────────────────────────────
        Section(scroll, "Presence", x, ref y);

        FieldLabel(scroll, "Session type", x, ref y);
        _cmbSession = Cmb(scroll, x, y, 180, SessionKeys, 0);
        y += 32;

        // BrickRow height: PoolY(46) + max 3 pool rows (16 bricks / ~6 per row) * 28px + 4 = 134px.
        // Use a fixed allocation so controls below don't shift as bricks move between rows.
        const int BrickRowAlloc = 134;

        FieldLabel(scroll, "Details", x, ref y);
        _brDetails = new BrickRow(" - ") { Left = x, Top = y, Width = 484, BackColor = BgForm };
        scroll.Controls.Add(_brDetails);
        y += BrickRowAlloc + 8;

        FieldLabel(scroll, "State", x, ref y);
        _brState = new BrickRow(" | ") { Left = x, Top = y, Width = 484, BackColor = BgForm };
        scroll.Controls.Add(_brState);
        y += BrickRowAlloc + 8;

        // Preview
        var preview = new Panel { Left = x, Top = y, Width = 484, Height = 66, BackColor = Color.FromArgb(30, 31, 34) };
        preview.Controls.Add(new Label { Text = "iRacing", Left = 10, Top = 7, ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoSize = true });
        _previewDetails = new Label { Left = 10, Top = 26, Width = 462, Height = 16, ForeColor = TextPrimary, AutoEllipsis = true, AutoSize = false };
        _previewState   = new Label { Left = 10, Top = 44, Width = 462, Height = 16, ForeColor = TextMuted,   AutoEllipsis = true, AutoSize = false };
        preview.Controls.Add(_previewDetails);
        preview.Controls.Add(_previewState);
        scroll.Controls.Add(preview);
        y += 74;

        _cbElapsedTimer = Cb(scroll, "Show elapsed session timer", current.ShowElapsedTimer, x, ref y);

        // ── Icons section ────────────────────────────────────────
        Divider(scroll, x, ref y);
        Section(scroll, "Icons", x, ref y);

        FieldLabel(scroll, "Large icon", x, ref y);
        _cmbLargeIcon = Cmb(scroll, x, y, 200, ["iRacing logo", "iRPC logo", "Track logo"], (int)current.LargeIcon);
        y += 32;
        FieldLabel(scroll, "Large image text", x, ref y);
        _brLargeText = new BrickRow(" | ") { Left = x, Top = y, Width = 484, BackColor = BgForm };
        _brLargeText.SetFromTemplate(current.LargeTextTemplate);
        scroll.Controls.Add(_brLargeText);
        y += BrickRowAlloc + 8;

        FieldLabel(scroll, "Small icon", x, ref y);
        _cmbSmallIcon = Cmb(scroll, x, y, 200, ["Off", "Car brand", "Session type"], (int)current.SmallIcon);
        y += 32;
        FieldLabel(scroll, "Small image text", x, ref y);
        _brSmallText = new BrickRow(" | ") { Left = x, Top = y, Width = 484, BackColor = BgForm };
        _brSmallText.SetFromTemplate(current.SmallTextTemplate);
        scroll.Controls.Add(_brSmallText);
        y += BrickRowAlloc + 8;

        // ── App section ──────────────────────────────────────────
        Divider(scroll, x, ref y);
        Section(scroll, "App", x, ref y);

        FieldLabel(scroll, "Discord App ID", x, ref y);
        _appIdBox = Tb(scroll, x, y, 300, current.DiscordAppId);
        y += 32;

        _cbLaunchOnStartup          = Cb(scroll, "Launch on Windows startup",              current.LaunchOnStartup,          x, ref y);
        _cbShowGitHubButton         = Cb(scroll, "Show GitHub button",                     current.ShowGitHubButton,         x, ref y);
        _cbAutoPopulateKeyOverrides = Cb(scroll, "Auto-populate key overrides from tracks", current.AutoPopulateKeyOverrides, x, ref y);

        // ── Debug section ────────────────────────────────────────
        Divider(scroll, x, ref y);
        Section(scroll, "Debug", x, ref y);

        _cbDebugMode = Cb(scroll, "Debug logging  (writes iRPC.log)", current.DebugMode, x, ref y);
        _cbTrackAndCarLogging = Cb(scroll, "Log new tracks/cars  (writes tracks.txt / cars.txt)", current.TrackAndCarLogging, x, ref y);

        var btnSnap = new Button
        {
            Text = "Export Telemetry Snapshot",
            Left = x, Top = y, Width = 210, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btnSnap.FlatAppearance.BorderSize = 0;
        btnSnap.Click += OnExportTelemetry;
        scroll.Controls.Add(btnSnap);
        y += 34;

        // ── Bottom bar ───────────────────────────────────────────
        var bar = new Panel { Left = 0, Top = 666, Width = 520, Height = 44, BackColor = Color.FromArgb(30, 31, 34) };

        string ver = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is System.Reflection.AssemblyInformationalVersionAttribute[] { Length: > 0 } a
            ? a[0].InformationalVersion : "?";
        bar.Controls.Add(new Label { Text = $"v{ver}", Left = 12, Top = 14, AutoSize = true, ForeColor = TextMuted });

        _lblSaved = new Label
        {
            Text = "✓  Saved", Left = 190, Top = 11, Width = 100, Height = 22,
            AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = GreenSaved, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Visible = false,
        };
        bar.Controls.Add(_lblSaved);

        var btnSave  = MakeButton("Save",  BgAccent, 354);
        var btnClose = MakeButton("Close", BgClose,  438);
        btnClose.DialogResult = DialogResult.Cancel;
        btnSave.Click += OnSave;
        bar.Controls.AddRange([btnSave, btnClose]);
        Controls.Add(bar);
        CancelButton = btnClose;

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _fadeTimer.Tick += OnFadeTick;

        // ── Wire events ──────────────────────────────────────────
        _cmbSession.SelectedIndexChanged += OnSessionChanged;
        _brDetails.Changed += UpdatePreview;
        _brState.Changed   += UpdatePreview;

        LoadSessionTemplate();
        UpdatePreview();
    }

    // ── Logic ────────────────────────────────────────────────────

    private void LoadSessionTemplate()
    {
        var cfg = _templates.GetValueOrDefault(_currentSessionKey, new());
        _brDetails.SetFromTemplate(cfg.DetailsTemplate);
        _brState.SetFromTemplate(cfg.StateTemplate);
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        _templates[_currentSessionKey] = new SessionPresenceConfig
        {
            DetailsTemplate = _brDetails.GetTemplate(),
            StateTemplate   = _brState.GetTemplate(),
        };
        _currentSessionKey = SessionKeys[_cmbSession.SelectedIndex];
        LoadSessionTemplate();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var data = new SessionData
        {
            IsConnected = true, IsOnTrack = true,
            SessionType = _currentSessionKey,
            TrackName = "Spa-Francorchamps", TrackConfig = "Full",
            CarName = "Ferrari 488 GT3",
            Position = 3, CurrentLap = 12, LapsRemain = 8,
            TimeRemaining = 2754, Speed = 50f, FuelLevel = 45.2f, FuelPercent = 0.6f,
        };
        _previewDetails.Text = DiscordService.ApplyTemplate(_brDetails.GetTemplate(), data);
        _previewState.Text   = DiscordService.ApplyTemplate(_brState.GetTemplate(), data);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        _templates[_currentSessionKey] = new SessionPresenceConfig
        {
            DetailsTemplate = _brDetails.GetTemplate(),
            StateTemplate   = _brState.GetTemplate(),
        };
        Settings = new AppSettings
        {
            DiscordAppId             = _appIdBox.Text.Trim(),
            LargeIcon                = (LargeIconMode)_cmbLargeIcon.SelectedIndex,
            SmallIcon                = (SmallIconMode)_cmbSmallIcon.SelectedIndex,
            LargeTextTemplate        = _brLargeText.GetTemplate(),
            SmallTextTemplate        = _brSmallText.GetTemplate(),
            ShowElapsedTimer         = _cbElapsedTimer.Checked,
            LaunchOnStartup          = _cbLaunchOnStartup.Checked,
            ShowGitHubButton         = _cbShowGitHubButton.Checked,
            AutoPopulateKeyOverrides = _cbAutoPopulateKeyOverrides.Checked,
            DebugMode                = _cbDebugMode.Checked,
            TrackAndCarLogging       = _cbTrackAndCarLogging.Checked,
            SessionTemplates         = _templates,
        };
        Settings.Save();
        _onSave(Settings);
        ShowSavedFeedback();
    }

    private void OnExportTelemetry(object? sender, EventArgs e)
    {
        var polls = _getTelemetry();
        if (polls.Count == 0)
        {
            MessageBox.Show("No telemetry data yet — open iRacing first.",
                "iRPC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"iRPC Telemetry Snapshot — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('─', 52));
        sb.AppendLine($"Last {polls.Count} poll tick(s) at 1 Hz");

        foreach (var (time, d) in polls)
        {
            sb.AppendLine();
            sb.AppendLine($"[{time:HH:mm:ss}]");
            sb.AppendLine($"  Connected    {d.IsConnected}");
            sb.AppendLine($"  On Track     {d.IsOnTrack}");
            sb.AppendLine($"  Is Replay    {d.IsReplay}");
            sb.AppendLine($"  Session      {d.SessionType}");
            sb.AppendLine($"  Track        {d.TrackName}{(d.TrackConfig.Length > 0 ? $" / {d.TrackConfig}" : "")}");
            sb.AppendLine($"  Car          {d.CarName}");
            sb.AppendLine($"  Position     {(d.Position > 0 ? $"P{d.Position}" : "—")}");
            sb.AppendLine($"  Lap          {d.CurrentLap}{(d.LapsRemain is > 0 and < 32767 ? $" / {d.CurrentLap + d.LapsRemain}  ({d.LapsRemain} left)" : "")}");
            sb.AppendLine($"  Time Left    {(d.TimeRemaining > 0 && d.TimeRemaining < 86400 ? TimeSpan.FromSeconds(d.TimeRemaining).ToString(@"h\:mm\:ss") : "—")}");
            sb.AppendLine($"  Speed        {d.Speed * 3.6f:F1} km/h  /  {d.Speed * 2.237f:F1} mph");
            sb.AppendLine($"  Fuel         {d.FuelLevel:F2} L  ({d.FuelPercent * 100:F1}%)");
            sb.AppendLine($"  Caution      {d.IsCaution}");
            sb.AppendLine($"  Checkered    {d.IsCheckered}");
            sb.AppendLine($"  On Pit Road  {d.OnPitRoad}");
            sb.AppendLine($"  In Garage    {d.IsInGarage}");
        }

        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "iRPC", "telemetry_snapshot.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = path, UseShellExecute = true });
    }

    private void ShowSavedFeedback()
    {
        _fadeStep = 0;
        _lblSaved.ForeColor = GreenSaved;
        _lblSaved.Visible = true;
        _fadeTimer.Stop();
        _fadeTimer.Start();
    }

    private static readonly Color BgBar = Color.FromArgb(30, 31, 34);

    private void OnFadeTick(object? sender, EventArgs e)
    {
        _fadeStep++;
        if (_fadeStep >= 60) { _lblSaved.Visible = false; _fadeTimer.Stop(); return; }
        float t = Math.Clamp((_fadeStep - 20) / 40f, 0f, 1f);
        _lblSaved.ForeColor = Color.FromArgb(
            (int)(GreenSaved.R + (BgBar.R - GreenSaved.R) * t),
            (int)(GreenSaved.G + (BgBar.G - GreenSaved.G) * t),
            (int)(GreenSaved.B + (BgBar.B - GreenSaved.B) * t));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _fadeTimer.Dispose();
        base.Dispose(disposing);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static void Section(Panel p, string text, int x, ref int y)
    {
        p.Controls.Add(new Label { Text = text, Left = x, Top = y, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), AutoSize = true });
        y += 24;
    }

    private static void FieldLabel(Panel p, string text, int x, ref int y)
    {
        p.Controls.Add(new Label { Text = text, Left = x, Top = y, ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5f), AutoSize = true });
        y += 15;
    }

    private static void Divider(Panel p, int x, ref int y)
    {
        y += 6;
        p.Controls.Add(new Panel { Left = x, Top = y, Width = 484, Height = 1, BackColor = BgDivider });
        y += 10;
    }

    private static TextBox Tb(Panel p, int x, int y, int w, string text = "")
    {
        var tb = new TextBox { Left = x, Top = y, Width = w, Text = text, BackColor = BgInput, ForeColor = TextPrimary, BorderStyle = BorderStyle.FixedSingle };
        p.Controls.Add(tb);
        return tb;
    }

    private static DarkDropDown Cmb(Panel p, int x, int y, int w, string[] items, int sel)
    {
        var c = new DarkDropDown(items, sel) { Left = x, Top = y, Width = w };
        p.Controls.Add(c);
        return c;
    }

    private static CheckBox Cb(Panel p, string text, bool val, int x, ref int y)
    {
        var cb = new CheckBox { Text = text, Left = x, Top = y, AutoSize = true, Checked = val, ForeColor = TextPrimary };
        p.Controls.Add(cb);
        y += cb.PreferredSize.Height + 4;
        return cb;
    }

    private static Button MakeButton(string text, Color bg, int left)
    {
        var btn = new Button
        {
            Text = text, Left = left, Top = 8, Width = 76, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}

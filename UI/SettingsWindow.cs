using System.Drawing;
using System.Drawing.Drawing2D; // Region, GraphicsPath, SmoothingMode used in UpdateLargeIconRegion
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
    private Dictionary<string, PresencePreset> _presets;
    private string _currentSessionKey = "Practice";

    private readonly Button _btnSave;
    private readonly System.Windows.Forms.Timer _savedResetTimer;

    private readonly DarkDropDown   _cmbPreset;
    private readonly Button         _btnLoadPreset;
    private readonly Button         _btnSavePreset;
    private readonly Button         _btnDeletePreset;
    private readonly DarkDropDown   _cmbSession;
    private readonly ITemplateEditor _brDetails;
    private readonly ITemplateEditor _brState;
    private readonly Label           _previewDetails;
    private readonly Label           _previewState;
    private readonly PictureBox      _prevLargeIcon;
    private readonly PictureBox      _prevSmallIcon;
    private static readonly System.Net.Http.HttpClient _http = new();
    private string? _prevLargeUrl;
    private string? _prevSmallUrl;
    private readonly DarkDropDown    _cmbLargeIcon;
    private readonly ITemplateEditor _brLargeText;
    private readonly DarkDropDown    _cmbSmallIcon;
    private readonly ITemplateEditor _brSmallText;
    private Panel? _scroll;
    private readonly TextBox  _appIdBox;
    private readonly CheckBox _cbLaunchOnStartup;
    private readonly CheckBox _cbCheckForUpdatesOnStartup;
    private readonly CheckBox _cbShowGitHubButton;
    private readonly CheckBox _cbDebugMode;
    private readonly CheckBox _cbTrackAndCarLogging;
    private readonly CheckBox _cbClassicEditor;
    private readonly NumericUpDown _nudIRatingWindow;
    private readonly DataGridView _dgvOverrides;

    private readonly Label _btnResetDetails;
    private readonly Label _btnResetState;
    private readonly Label _btnResetPresence;
    private readonly Label _btnResetIcons;
    private readonly Label _btnResetLargeText;
    private readonly Label _btnResetSmallText;
    private readonly ToolTip _resetTip = new();

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

        _presets = current.Presets.ToDictionary(kv => kv.Key, kv => new PresencePreset
        {
            SessionTemplates = kv.Value.SessionTemplates.ToDictionary(
                t => t.Key,
                t => new SessionPresenceConfig { DetailsTemplate = t.Value.DetailsTemplate, StateTemplate = t.Value.StateTemplate }),
            LargeTextTemplate = kv.Value.LargeTextTemplate,
            SmallTextTemplate = kv.Value.SmallTextTemplate,
        });

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
        int presenceY = y;
        Section(scroll, "Presence", x, ref y);
        _btnResetPresence = CreateResetButton(scroll,
            x + TextRenderer.MeasureText("Presence", new Font("Segoe UI", 9.5f, FontStyle.Bold)).Width + 8,
            presenceY + 4);
        _resetTip.SetToolTip(_btnResetPresence, "Reset all session type templates to their defaults");
        _btnResetPresence.Click += OnResetPresence;

        FieldLabel(scroll, "Preset", x, ref y);
        _cmbPreset = Cmb(scroll, x, y, 196, _presets.Count > 0 ? [.. _presets.Keys] : ["(no presets)"], 0);
        _btnLoadPreset   = MakeSmallButton("Load",     x + 204, y);
        _btnSavePreset   = MakeSmallButton("Save as",  x + 268, y);
        _btnDeletePreset = MakeSmallButton("Delete",   x + 352, y);
        _btnLoadPreset.Enabled   = _presets.Count > 0;
        _btnDeletePreset.Enabled = _presets.Count > 0;
        _btnLoadPreset.Click   += OnLoadPreset;
        _btnSavePreset.Click   += OnSavePreset;
        _btnDeletePreset.Click += OnDeletePreset;
        scroll.Controls.AddRange([_btnLoadPreset, _btnSavePreset, _btnDeletePreset]);
        y += 32;

        FieldLabel(scroll, "Session type", x, ref y);
        _cmbSession = Cmb(scroll, x, y, 180, SessionKeys, 0);
        y += 32;

        bool expEditor  = !current.ClassicTemplateEditor;
        int  editorAlloc = expEditor ? (24 + 4) : BrickRow.MaxHeight(484);

        _btnResetDetails = FieldLabelWithReset(scroll, "Details", x, ref y);
        _resetTip.SetToolTip(_btnResetDetails, "Reset to default for this session type");
        _brDetails = MakeEditor(expEditor, " - ", x, y, 484, BgForm);
        scroll.Controls.Add((Control)_brDetails);
        if (expEditor) WireChipReflow((Control)_brDetails);
        y += editorAlloc + 8;
        _btnResetDetails.Click += (_, _) =>
        {
            string key = _currentSessionKey;
            if (MessageBox.Show($"Reset the Details template for {key} to default?\n\nThis will undo your custom changes.",
                    "iRPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (AppSettings.DefaultTemplates.TryGetValue(key, out var def))
                _brDetails.SetFromTemplate(def.DetailsTemplate);
            UpdatePreview();
        };

        _btnResetState = FieldLabelWithReset(scroll, "State", x, ref y);
        _resetTip.SetToolTip(_btnResetState, "Reset to default for this session type");
        _brState = MakeEditor(expEditor, " | ", x, y, 484, BgForm);
        scroll.Controls.Add((Control)_brState);
        if (expEditor) WireChipReflow((Control)_brState);
        y += editorAlloc + 8;
        _btnResetState.Click += (_, _) =>
        {
            string key = _currentSessionKey;
            if (MessageBox.Show($"Reset the State template for {key} to default?\n\nThis will undo your custom changes.",
                    "iRPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (AppSettings.DefaultTemplates.TryGetValue(key, out var def))
                _brState.SetFromTemplate(def.StateTemplate);
            UpdatePreview();
        };

        // Preview
        const int iconSize = 60, iconLeft = 10, textLeft = 78, textW = 394;
        var preview = new Panel { Left = x, Top = y, Width = 484, Height = 82, BackColor = Color.FromArgb(30, 31, 34) };

        _prevLargeIcon = new PictureBox
        {
            Left = iconLeft, Top = 11, Width = iconSize, Height = iconSize,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(30, 31, 34),
        };
        _prevSmallIcon = new PictureBox
        {
            Left = iconLeft + iconSize - 22, Top = 11 + iconSize - 22, Width = 22, Height = 22,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(30, 31, 34),
            Visible = false,
        };
        var smallIconPath = new GraphicsPath();
        const int sr = 5;
        smallIconPath.AddArc(0,       0,       sr * 2, sr * 2, 180, 90);
        smallIconPath.AddArc(22-sr*2, 0,       sr * 2, sr * 2, 270, 90);
        smallIconPath.AddArc(22-sr*2, 22-sr*2, sr * 2, sr * 2,   0, 90);
        smallIconPath.AddArc(0,       22-sr*2, sr * 2, sr * 2,  90, 90);
        smallIconPath.CloseFigure();
        _prevSmallIcon.Region = new Region(smallIconPath);

        preview.Controls.Add(_prevLargeIcon);
        preview.Controls.Add(_prevSmallIcon);
        preview.Controls.Add(new Label { Text = "iRacing", Left = textLeft, Top = 9, ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoSize = true });
        _prevSmallIcon.BringToFront();
        _previewDetails = new Label { Left = textLeft, Top = 28, Width = textW, Height = 16, ForeColor = TextPrimary, AutoEllipsis = true, AutoSize = false };
        _previewState   = new Label { Left = textLeft, Top = 48, Width = textW, Height = 16, ForeColor = TextMuted,   AutoEllipsis = true, AutoSize = false };
        preview.Controls.Add(_previewDetails);
        preview.Controls.Add(_previewState);
        scroll.Controls.Add(preview);
        y += 90;

        // ── Icons section ────────────────────────────────────────
        Divider(scroll, x, ref y);
        int iconsY = y;
        Section(scroll, "Icons", x, ref y);
        _btnResetIcons = CreateResetButton(scroll,
            x + TextRenderer.MeasureText("Icons", new Font("Segoe UI", 9.5f, FontStyle.Bold)).Width + 8,
            iconsY + 4);
        _resetTip.SetToolTip(_btnResetIcons, "Reset all icon settings to defaults");
        _btnResetIcons.Click += OnResetIcons;

        FieldLabel(scroll, "Large icon", x, ref y);
        _cmbLargeIcon = Cmb(scroll, x, y, 200, ["iRacing logo", "iRPC logo", "Track logo"], (int)current.LargeIcon);
        y += 32;
        _btnResetLargeText = FieldLabelWithReset(scroll, "Large image text", x, ref y);
        _resetTip.SetToolTip(_btnResetLargeText, "Reset to default");
        _brLargeText = MakeEditor(expEditor, " | ", x, y, 484, BgForm);
        _brLargeText.SetFromTemplate(current.LargeTextTemplate);
        scroll.Controls.Add((Control)_brLargeText);
        if (expEditor) WireChipReflow((Control)_brLargeText);
        _brLargeText.Changed += UpdatePreview;
        _btnResetLargeText.Click += (_, _) =>
        {
            if (MessageBox.Show("Reset Large image text to default?\n\nThis will undo your custom changes.",
                    "iRPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _brLargeText.SetFromTemplate("{track} - {config}");
            UpdatePreview();
        };
        y += editorAlloc + 8;

        FieldLabel(scroll, "Small icon", x, ref y);
        _cmbSmallIcon = Cmb(scroll, x, y, 200, ["Off", "Car brand", "Session type"], (int)current.SmallIcon);
        y += 32;
        _btnResetSmallText = FieldLabelWithReset(scroll, "Small image text", x, ref y);
        _resetTip.SetToolTip(_btnResetSmallText, "Reset to default");
        _brSmallText = MakeEditor(expEditor, " | ", x, y, 484, BgForm);
        _brSmallText.SetFromTemplate(current.SmallTextTemplate);
        scroll.Controls.Add((Control)_brSmallText);
        if (expEditor) WireChipReflow((Control)_brSmallText);
        _brSmallText.Changed += UpdatePreview;
        _btnResetSmallText.Click += (_, _) =>
        {
            if (MessageBox.Show("Reset Small image text to default?\n\nThis will undo your custom changes.",
                    "iRPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _brSmallText.SetFromTemplate("{car}");
            UpdatePreview();
        };
        y += editorAlloc + 8;

        // ── App section ──────────────────────────────────────────
        Divider(scroll, x, ref y);
        Section(scroll, "App", x, ref y);

        FieldLabel(scroll, "Discord App ID", x, ref y);
        _appIdBox = Tb(scroll, x, y, 300, current.DiscordAppId);
        y += 32;

        _cbLaunchOnStartup          = Cb(scroll, "Launch on Windows startup",              current.LaunchOnStartup,              x, ref y);
        _cbCheckForUpdatesOnStartup = Cb(scroll, "Check for updates on startup",           current.CheckForUpdatesOnStartup,     x, ref y);
        _cbShowGitHubButton         = Cb(scroll, "Show GitHub button",                     current.ShowGitHubButton,             x, ref y);
        _cbClassicEditor       = Cb(scroll, "Classic brick-style template editor (takes effect on reopen)", current.ClassicTemplateEditor, x, ref y);

        FieldLabel(scroll, "Custom iRating avg window (races)", x, ref y);
        _nudIRatingWindow = Nud(scroll, x, y, 80, current.IRatingAvgCustomWindow);
        y += 32;

        // ── Key Overrides section ────────────────────────────────
        Divider(scroll, x, ref y);
        Section(scroll, "Key Overrides", x, ref y);
        scroll.Controls.Add(new Label
        {
            Text = "Remap auto-generated asset keys to custom filenames. Built-in defaults are applied automatically and don't appear here.",
            Left = x, Top = y, Width = 484, AutoSize = false, Height = 30,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5f),
        });
        y += 34;

        _dgvOverrides = new DataGridView
        {
            Left = x, Top = y, Width = 484, Height = 140,
            BackgroundColor = BgInput, ForeColor = TextPrimary, GridColor = BgDivider,
            BorderStyle = BorderStyle.None, RowHeadersVisible = false,
            AllowUserToResizeRows = false, AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgInput, ForeColor = TextPrimary,
                SelectionBackColor = BgAccent, SelectionForeColor = Color.White,
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = BgClose, ForeColor = TextMuted, SelectionBackColor = BgClose,
            },
            ColumnHeadersHeight = 24,
        };
        _dgvOverrides.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key",    HeaderText = "Asset key",      FillWeight = 50 });
        _dgvOverrides.Columns.Add(new DataGridViewTextBoxColumn { Name = "Target", HeaderText = "Maps to",        FillWeight = 50 });
        foreach (var kv in KeyOverrides.GetAll())
            _dgvOverrides.Rows.Add(kv.Key, kv.Value);
        scroll.Controls.Add(_dgvOverrides);
        y += 148;

        var btnAddOverride = new Button
        {
            Text = "+ Add row", Left = x, Top = y, Width = 90, Height = 24,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 8.5f), Cursor = Cursors.Hand,
        };
        btnAddOverride.FlatAppearance.BorderSize = 0;
        btnAddOverride.Click += (_, _) =>
        {
            int row = _dgvOverrides.Rows.Add("", "");
            _dgvOverrides.CurrentCell = _dgvOverrides.Rows[row].Cells[0];
            _dgvOverrides.BeginEdit(true);
        };
        var btnDelOverride = new Button
        {
            Text = "Delete row", Left = x + 98, Top = y, Width = 90, Height = 24,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 8.5f), Cursor = Cursors.Hand,
        };
        btnDelOverride.FlatAppearance.BorderSize = 0;
        btnDelOverride.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in _dgvOverrides.SelectedRows)
                if (!row.IsNewRow) _dgvOverrides.Rows.Remove(row);
        };
        scroll.Controls.AddRange([btnAddOverride, btnDelOverride]);
        y += 32;

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

        var btnScan = new Button
        {
            Text = "Scan Installed Content (.dat paths)",
            Left = x, Top = y, Width = 230, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btnScan.FlatAppearance.BorderSize = 0;
        btnScan.Click += OnScanContent;
        scroll.Controls.Add(btnScan);
        y += 34;

        var btnOpenFolder = new Button
        {
            Text = "Open Data Folder",
            Left = x, Top = y, Width = 160, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btnOpenFolder.FlatAppearance.BorderSize = 0;
        btnOpenFolder.Click += (_, _) =>
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iRPC");
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start("explorer.exe", folder);
        };
        scroll.Controls.Add(btnOpenFolder);
        y += 34;

        // ── Bottom bar ───────────────────────────────────────────
        var bar = new Panel { Left = 0, Top = 666, Width = 520, Height = 44, BackColor = Color.FromArgb(30, 31, 34) };

        string ver = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            is System.Reflection.AssemblyInformationalVersionAttribute[] { Length: > 0 } a
            ? a[0].InformationalVersion : "?";
        bar.Controls.Add(new Label { Text = $"v{ver}", Left = 12, Top = 14, AutoSize = true, ForeColor = TextMuted });

        _btnSave  = MakeButton("Save",  BgAccent, 354);
        var btnClose = MakeButton("Close", BgClose,  438);
        btnClose.Click += (_, _) => Close();
        _btnSave.Click += OnSave;
        bar.Controls.AddRange([_btnSave, btnClose]);
        Controls.Add(bar);
        CancelButton = btnClose;

        _savedResetTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _savedResetTimer.Tick += (_, _) =>
        {
            _btnSave.Text = "Save";
            _btnSave.BackColor = BgAccent;
            _savedResetTimer.Stop();
        };

        // ── Wire events ──────────────────────────────────────────
        _cmbSession.SelectedIndexChanged   += OnSessionChanged;
        _cmbLargeIcon.SelectedIndexChanged += (_, _) => UpdatePreview();
        _cmbSmallIcon.SelectedIndexChanged += (_, _) => UpdatePreview();
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
            LastLapTime = 92.456f, BestLapTime = 91.987f,
            StrengthOfField = 2450,
            PlayerIRating = 2450, IRatingAvg5 = 2410, IRatingAvg10 = 2390,
            IRatingAvgCustom = 2375, IRatingAvgCustomWindow = Settings.IRatingAvgCustomWindow,
            ClassPosition = 2, AirTempC = 24f, TrackTempC = 32f, Skies = 1,
            OnPitRoad = true, PitstopActive = true,
            PitRepairLeft = 12f, PitOptRepairLeft = 8f,
            FastRepairsUsed = 1, FastRepairsAvailable = 3, IncidentCount = 3,
            TireCompound = "Soft",
        };
        _previewDetails.Text = DiscordService.ApplyTemplate(_brDetails.GetTemplate(), data);
        _previewState.Text   = DiscordService.ApplyTemplate(_brState.GetTemplate(), data);

        string largeUrl = PreviewLargeUrl;
        if (largeUrl != _prevLargeUrl) { _prevLargeUrl = largeUrl; LoadPreviewIcon(_prevLargeIcon, largeUrl); }
        string? smallUrl = PreviewSmallUrl;
        _prevSmallIcon.Visible = smallUrl is not null;
        if (smallUrl is not null && smallUrl != _prevSmallUrl) { _prevSmallUrl = smallUrl; LoadPreviewIcon(_prevSmallIcon, smallUrl); }
        UpdateLargeIconRegion();

        UpdateResetButtons();
    }

    private void UpdateLargeIconRegion()
    {
        if (_prevSmallIcon.Visible)
        {
            // Punch a rounded-rect hole in the large icon where the small icon overlaps,
            // with a 2px gap acting as a border — matching Discord's composite rendering.
            var region = new Region(new Rectangle(0, 0, 60, 60));
            // Small icon sits at (38,38) within the 60x60 large icon; expand 2px for the gap.
            const int cx = 36, cy = 36, cw = 26, ch = 26, cr = 7;
            var cutout = new GraphicsPath();
            cutout.AddArc(cx,          cy,          cr * 2, cr * 2, 180, 90);
            cutout.AddArc(cx + cw - cr * 2, cy,          cr * 2, cr * 2, 270, 90);
            cutout.AddArc(cx + cw - cr * 2, cy + ch - cr * 2, cr * 2, cr * 2,   0, 90);
            cutout.AddArc(cx,          cy + ch - cr * 2, cr * 2, cr * 2,  90, 90);
            cutout.CloseFigure();
            region.Exclude(cutout);
            _prevLargeIcon.Region = region;
        }
        else
        {
            _prevLargeIcon.Region = null;
        }
    }

    private void UpdateResetButtons()
    {
        var defaults = AppSettings.DefaultTemplates;
        string currentDetails = _brDetails.GetTemplate();
        string currentState   = _brState.GetTemplate();

        bool detailsDirty = defaults.TryGetValue(_currentSessionKey, out var def)
            && currentDetails != def.DetailsTemplate;
        bool stateDirty = defaults.TryGetValue(_currentSessionKey, out def)
            && currentState != def.StateTemplate;

        _btnResetDetails.Visible = detailsDirty;
        _btnResetState.Visible   = stateDirty;

        bool anyDirty = detailsDirty || stateDirty;
        if (!anyDirty)
        {
            foreach (var kv in defaults)
            {
                if (kv.Key == _currentSessionKey) continue;
                if (!_templates.TryGetValue(kv.Key, out var t)) continue;
                if (t.DetailsTemplate != kv.Value.DetailsTemplate || t.StateTemplate != kv.Value.StateTemplate)
                {
                    anyDirty = true;
                    break;
                }
            }
        }
        _btnResetPresence.Visible = anyDirty;

        bool largeTextDirty = _brLargeText.GetTemplate() != "{track} - {config}";
        bool smallTextDirty = _brSmallText.GetTemplate() != "{car}";
        bool iconModeDirty  = _cmbLargeIcon.SelectedIndex != (int)LargeIconMode.TrackLogo
                           || _cmbSmallIcon.SelectedIndex != (int)SmallIconMode.CarBrand;
        _btnResetLargeText.Visible = largeTextDirty;
        _btnResetSmallText.Visible = smallTextDirty;
        _btnResetIcons.Visible = largeTextDirty || smallTextDirty || iconModeDirty;
    }

    private void OnResetIcons(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            "This will reset all icon settings to their defaults.\n\nAre you sure?",
            "iRPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        _cmbLargeIcon.SelectedIndex = (int)LargeIconMode.TrackLogo;
        _cmbSmallIcon.SelectedIndex = (int)SmallIconMode.CarBrand;
        _brLargeText.SetFromTemplate("{track} - {config}");
        _brSmallText.SetFromTemplate("{car}");
        UpdatePreview();
    }

    private void OnResetPresence(object? sender, EventArgs e)
    {
        var answer = MessageBox.Show(
            "This will reset all session type templates to their defaults.\n\nAre you sure?",
            "iRPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        foreach (var kv in AppSettings.DefaultTemplates)
            _templates[kv.Key] = new SessionPresenceConfig
            {
                DetailsTemplate = kv.Value.DetailsTemplate,
                StateTemplate   = kv.Value.StateTemplate,
            };
        LoadSessionTemplate();
        UpdatePreview();
    }

    private void RefreshPresetDropdown(string? selectName = null)
    {
        bool any = _presets.Count > 0;
        string[] names = any ? [.. _presets.Keys] : ["(no presets)"];
        int sel = any && selectName != null ? Array.IndexOf(names, selectName) : 0;
        _cmbPreset.SetItems(names, Math.Max(sel, 0));
        _btnLoadPreset.Enabled   = any;
        _btnDeletePreset.Enabled = any;
    }

    private void OnLoadPreset(object? sender, EventArgs e)
    {
        string? name = _cmbPreset.SelectedItem;
        if (name is null || !_presets.TryGetValue(name, out var preset)) return;

        foreach (var kv in preset.SessionTemplates)
            _templates[kv.Key] = new SessionPresenceConfig
                { DetailsTemplate = kv.Value.DetailsTemplate, StateTemplate = kv.Value.StateTemplate };
        _brLargeText.SetFromTemplate(preset.LargeTextTemplate);
        _brSmallText.SetFromTemplate(preset.SmallTextTemplate);
        LoadSessionTemplate();
        UpdatePreview();
    }

    private void OnSavePreset(object? sender, EventArgs e)
    {
        _templates[_currentSessionKey] = new SessionPresenceConfig
            { DetailsTemplate = _brDetails.GetTemplate(), StateTemplate = _brState.GetTemplate() };

        string? name = PromptText("Preset name:", _cmbPreset.SelectedItem ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_presets.ContainsKey(name) &&
            MessageBox.Show($"Overwrite preset \"{name}\"?", "iRPC",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _presets[name] = new PresencePreset
        {
            SessionTemplates = _templates.ToDictionary(kv => kv.Key, kv => new SessionPresenceConfig
                { DetailsTemplate = kv.Value.DetailsTemplate, StateTemplate = kv.Value.StateTemplate }),
            LargeTextTemplate = _brLargeText.GetTemplate(),
            SmallTextTemplate = _brSmallText.GetTemplate(),
        };
        RefreshPresetDropdown(name);
    }

    private void OnDeletePreset(object? sender, EventArgs e)
    {
        string? name = _cmbPreset.SelectedItem;
        if (name is null || !_presets.ContainsKey(name)) return;
        if (MessageBox.Show($"Delete preset \"{name}\"?", "iRPC",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _presets.Remove(name);
        RefreshPresetDropdown();
    }

    private string? PromptText(string message, string defaultValue = "")
    {
        var form = new Form
        {
            Text = "iRPC", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false,
            ClientSize = new Size(320, 104), BackColor = BgForm,
        };
        form.Controls.Add(new Label { Text = message, Left = 12, Top = 12, AutoSize = true, ForeColor = TextPrimary });
        var tb = new TextBox { Left = 12, Top = 32, Width = 296, Text = defaultValue, BackColor = BgInput, ForeColor = TextPrimary, BorderStyle = BorderStyle.FixedSingle };
        var ok = new Button { Text = "OK", Left = 152, Top = 68, Width = 72, Height = 26, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = BgAccent, ForeColor = Color.White };
        ok.FlatAppearance.BorderSize = 0;
        var cancel = new Button { Text = "Cancel", Left = 236, Top = 68, Width = 72, Height = 26, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White };
        cancel.FlatAppearance.BorderSize = 0;
        form.Controls.AddRange([tb, ok, cancel]);
        form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(tb.Text) ? tb.Text.Trim() : null;
    }

    private string PreviewLargeUrl => (LargeIconMode)_cmbLargeIcon.SelectedIndex switch
    {
        LargeIconMode.IracingLogo => $"{DiscordService.AssetBase}/Icons/iracing_logo.png",
        LargeIconMode.IrpcLogo   => $"{DiscordService.AssetBase}/Icons/irpc_logo.png",
        _                        => $"{DiscordService.AssetBase}/Tracks/track_spa.png",
    };

    private string? PreviewSmallUrl => (SmallIconMode)_cmbSmallIcon.SelectedIndex switch
    {
        SmallIconMode.CarBrand    => $"{DiscordService.AssetBase}/Brands/brand_ferrari.png",
        SmallIconMode.SessionType => $"{DiscordService.AssetBase}/Icons/icon_{_currentSessionKey.ToLowerInvariant().Replace(" ", "_")}.png",
        _                         => null,
    };

    private async void LoadPreviewIcon(PictureBox pb, string url)
    {
        try
        {
            byte[] bytes = await _http.GetByteArrayAsync(url);
            if (pb.IsDisposed) return;
            pb.Invoke(() =>
            {
                pb.Image?.Dispose();
                pb.Image = System.Drawing.Image.FromStream(new System.IO.MemoryStream(bytes));
            });
        }
        catch { }
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
            LaunchOnStartup          = _cbLaunchOnStartup.Checked,
            CheckForUpdatesOnStartup = _cbCheckForUpdatesOnStartup.Checked,
            ShowGitHubButton             = _cbShowGitHubButton.Checked,
            ClassicTemplateEditor        = _cbClassicEditor.Checked,
            DebugMode                    = _cbDebugMode.Checked,
            TrackAndCarLogging       = _cbTrackAndCarLogging.Checked,
            IRatingAvgCustomWindow   = (int)_nudIRatingWindow.Value,
            SessionTemplates         = _templates,
            Presets                  = _presets,
        };
        Settings.Save();

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _dgvOverrides.Rows)
        {
            string key    = (row.Cells["Key"].Value?.ToString() ?? "").Trim();
            string target = (row.Cells["Target"].Value?.ToString() ?? "").Trim();
            if (key.Length > 0 && target.Length > 0)
                overrides[key] = target;
        }
        KeyOverrides.SetAll(overrides);

        try { _onSave(Settings); }
        finally { ShowSavedFeedback(); }
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
            sb.AppendLine($"  Track Code   {(d.TrackCodeName.Length > 0 ? d.TrackCodeName : "—")}");
            sb.AppendLine($"  Car          {d.CarName}");
            sb.AppendLine($"  Car Code     {(d.CarCodeName.Length > 0 ? d.CarCodeName : "—")}");
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

    private void OnScanContent(object? sender, EventArgs e)
    {
        const string defaultRoot = @"C:\Program Files (x86)\iRacing";
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select your iRacing install folder (contains 'cars' and 'tracks' subfolders)",
            SelectedPath = Directory.Exists(defaultRoot) ? defaultRoot : "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string path = ContentScanner.Scan(dlg.SelectedPath);
        MessageBox.Show($"Scan complete — saved to:\n{path}\n\nSend me that file and I'll match the paths up to asset overrides.",
            "iRPC", MessageBoxButtons.OK, MessageBoxIcon.Information);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = path, UseShellExecute = true });
    }

    private void ShowSavedFeedback()
    {
        _btnSave.Text = "✓ Saved";
        _btnSave.BackColor = GreenSaved;
        _savedResetTimer.Stop();
        _savedResetTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _savedResetTimer.Dispose();
            _resetTip.Dispose();
            _prevLargeIcon.Image?.Dispose();
            _prevSmallIcon.Image?.Dispose();
        }
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

    private static NumericUpDown Nud(Panel p, int x, int y, int w, int val)
    {
        var nud = new NumericUpDown
        {
            Left = x, Top = y, Width = w, Minimum = 1, Maximum = 200, Value = Math.Clamp(val, 1, 200),
            BackColor = BgInput, ForeColor = TextPrimary, BorderStyle = BorderStyle.FixedSingle,
        };
        p.Controls.Add(nud);
        return nud;
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

    private Label FieldLabelWithReset(Panel p, string text, int x, ref int y)
    {
        var font = new Font("Segoe UI", 8.5f);
        p.Controls.Add(new Label { Text = text, Left = x, Top = y, ForeColor = TextMuted, Font = font, AutoSize = true });
        int lblW = TextRenderer.MeasureText(text, font).Width;
        var lbl  = CreateResetButton(p, x + lblW + 6, y);
        y += 15;
        return lbl;
    }

    private static Label CreateResetButton(Panel p, int x, int y)
    {
        var lbl = new Label
        {
            Text = "Reset",
            Left = x, Top = y,
            AutoSize = true,
            ForeColor = Color.FromArgb(110, 115, 125),
            BackColor = BgForm,
            Font = new Font("Segoe UI", 7.5f),
            Cursor = Cursors.Hand,
            Visible = false,
        };
        lbl.MouseEnter += (_, _) => lbl.ForeColor = Color.FromArgb(200, 210, 220);
        lbl.MouseLeave += (_, _) => lbl.ForeColor = Color.FromArgb(110, 115, 125);
        p.Controls.Add(lbl);
        return lbl;
    }

    // When a chip editor wraps to an additional row its Height grows. Shift every sibling
    // control in the scroll panel that was below the editor's old bottom edge down by the delta.
    private void WireChipReflow(Control editor)
    {
        int prevH = editor.Height;
        editor.SizeChanged += (_, _) =>
        {
            int delta = editor.Height - prevH;
            if (delta == 0) return;
            int oldBottom = editor.Top + prevH;
            foreach (Control c in _scroll!.Controls)
            {
                if (c != editor && c.Top >= oldBottom)
                    c.Top += delta;
            }
            prevH = editor.Height;
        };
    }

    private static ITemplateEditor MakeEditor(bool chip, string sep, int left, int top, int width, Color bg)
    {
        if (chip)
            return new ChipTemplateEditor(sep) { Left = left, Top = top, Width = width, BackColor = bg };
        return new BrickRow(sep) { Left = left, Top = top, Width = width, BackColor = bg };
    }

    private static Button MakeSmallButton(string text, int left, int top)
    {
        var btn = new Button
        {
            Text = text, Left = left, Top = top, Width = 76, Height = 23,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 8.5f), Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
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

using System.Drawing;
using System.Windows.Forms;

namespace iRPC;

public class WelcomeWizard : Form
{
    private static readonly Color BgForm      = Color.FromArgb(43, 45, 49);
    private static readonly Color BgInput     = Color.FromArgb(24, 25, 28);
    private static readonly Color BgAccent    = Color.FromArgb(88, 101, 242);
    private static readonly Color BgClose     = Color.FromArgb(64, 66, 73);
    private static readonly Color TextPrimary = Color.FromArgb(219, 222, 225);
    private static readonly Color TextMuted   = Color.FromArgb(148, 155, 164);

    private static readonly SessionData PreviewData = new()
    {
        IsConnected = true, IsOnTrack = true,
        SessionType = "Race", TrackName = "Spa-Francorchamps", TrackConfig = "GP",
        CarName = "Porsche 911 GT3 R",
        Position = 3, CurrentLap = 10, LapsRemain = 10, TimeRemaining = 2700,
        FuelLevel = 40f, FuelPercent = 0.5f, LastLapTime = 142.5f, BestLapTime = 141.8f,
        StrengthOfField = 2480, PlayerIRating = 2150,
        AirTempC = 22f, TrackTempC = 28f, Skies = 0, IncidentCount = 2,
    };

    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["Minimal"]    = "Just the track name and car. Clean and distraction-free.",
        ["Standard"]   = "Track, car, position, laps, and time remaining. A solid all-rounder.",
        ["Endurance"]  = "Built for long races. Tracks fuel, tire compound, and lap times alongside position.",
        ["iRating"]    = "Stats-focused: iRating, strength of field, position, and incident count.",
        ["Weather"]    = "Highlights conditions: sky, air temp, track temp, and flag.",
        ["Oval"]       = "Built for oval racing: laps to go, fuel, incidents, and flag.",
        ["Qualifying"] = "What matters in quali: best lap, last lap, and your current position.",
        ["Spectator"]  = "Minimal info for watching: just the SoF and session type.",
    };

    private readonly AppSettings _settings;
    private readonly Panel _step0, _step1, _step2;
    private readonly Button _btnBack, _btnNext;
    private readonly ListBox _presetList;
    private readonly Label _previewDetails, _previewState, _presetDesc;
    private readonly CheckBox _cbStartup, _cbUpdates, _cbGitHub;
    private int _step;

    public WelcomeWizard(AppSettings settings)
    {
        _settings = settings;
        Text            = "iRPC Setup";
        ClientSize      = new Size(540, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = MinimizeBox = false;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = BgForm;
        ForeColor       = TextPrimary;

        var navBar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = BgInput };

        var btnSkip = new Button
        {
            Text = "Skip", Width = 60, Height = 30, Left = 16, Top = 11,
            FlatStyle = FlatStyle.Flat, BackColor = BgInput, ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btnSkip.FlatAppearance.BorderColor = btnSkip.FlatAppearance.MouseOverBackColor = BgInput;
        btnSkip.Click += (_, _) => Close();

        _btnBack = new Button
        {
            Text = "Back", Width = 80, Height = 32, Left = 342, Top = 10, Visible = false,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        _btnBack.FlatAppearance.BorderColor = BgClose;
        _btnBack.Click += (_, _) => { if (_step > 0) ShowStep(_step - 1); };

        _btnNext = new Button
        {
            Text = "Next", Width = 100, Height = 32, Left = 430, Top = 10,
            FlatStyle = FlatStyle.Flat, BackColor = BgAccent, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), Cursor = Cursors.Hand,
        };
        _btnNext.FlatAppearance.BorderColor = BgAccent;
        _btnNext.Click += OnNext;

        navBar.Controls.AddRange([btnSkip, _btnBack, _btnNext]);

        _step0 = BuildStep0();
        (_step1, _presetList, _previewDetails, _previewState, _presetDesc) = BuildStep1();
        (_step2, _cbStartup, _cbUpdates, _cbGitHub) = BuildStep2();

        Controls.AddRange([navBar, _step0, _step1, _step2]);

        _presetList.SelectedIndexChanged += (_, _) => UpdatePreview();
        int stdIdx = _presetList.Items.IndexOf("Standard");
        _presetList.SelectedIndex = stdIdx >= 0 ? stdIdx : 0;

        ShowStep(0);
    }

    private Panel BuildStep0()
    {
        var p = StepPanel();

        p.Controls.Add(new Label
        {
            Text = "Welcome to iRPC",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = true, Left = 40, Top = 64,
        });
        p.Controls.Add(new Label
        {
            Text = "iRPC shows your iRacing session as Discord Rich Presence,\n" +
                   "so your friends can see what track you're on, what car\n" +
                   "you're driving, and how the race is going in real time.",
            Font = new Font("Segoe UI", 11f), ForeColor = TextPrimary,
            AutoSize = true, Left = 40, Top = 132,
        });
        p.Controls.Add(new Label
        {
            Text = "Next, pick a starting preset that matches how you want to show up.",
            Font = new Font("Segoe UI", 10f), ForeColor = TextMuted,
            AutoSize = true, Left = 40, Top = 240,
        });
        return p;
    }

    private (Panel, ListBox, Label, Label, Label) BuildStep1()
    {
        var p = StepPanel();

        p.Controls.Add(new Label
        {
            Text = "Choose a starting preset",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = true, Left = 16, Top = 18,
        });

        var list = new ListBox
        {
            Left = 16, Top = 56, Width = 160, Height = 224,
            BackColor = BgInput, ForeColor = TextPrimary,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 28,
            Font = new Font("Segoe UI", 10f),
        };
        list.Items.AddRange([.. AppSettings.DefaultPresets.Keys]);
        list.DrawItem += DrawPresetItem;
        p.Controls.Add(list);

        // Discord presence preview card
        var card = new Panel { Left = 188, Top = 56, Width = 332, Height = 78, BackColor = Color.FromArgb(30, 31, 34) };
        card.Controls.Add(new Label { Text = "iRacing", Left = 12, Top = 8, ForeColor = Color.White, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoSize = true });
        var details = new Label { Left = 12, Top = 27, Width = 308, Height = 16, ForeColor = TextPrimary, AutoEllipsis = true, AutoSize = false, Font = new Font("Segoe UI", 9f) };
        var state   = new Label { Left = 12, Top = 47, Width = 308, Height = 16, ForeColor = TextMuted,   AutoEllipsis = true, AutoSize = false, Font = new Font("Segoe UI", 9f) };
        card.Controls.Add(details);
        card.Controls.Add(state);
        p.Controls.Add(card);

        var desc = new Label { Left = 188, Top = 148, Width = 332, Height = 40, ForeColor = TextMuted, AutoSize = false, Font = new Font("Segoe UI", 9f) };
        p.Controls.Add(desc);

        p.Controls.Add(new Label
        {
            Text = "You can switch presets anytime from the tray menu.",
            Font = new Font("Segoe UI", 9f), ForeColor = TextMuted,
            AutoSize = true, Left = 16, Top = 300,
        });

        return (p, list, details, state, desc);
    }

    private static void DrawPresetItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox lb || e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using var bg = new SolidBrush(sel ? BgAccent : BgInput);
        e.Graphics.FillRectangle(bg, e.Bounds);
        TextRenderer.DrawText(e.Graphics, lb.Items[e.Index]?.ToString() ?? "", e.Font,
            new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
            sel ? Color.White : TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    private (Panel, CheckBox, CheckBox, CheckBox) BuildStep2()
    {
        var p = StepPanel();

        p.Controls.Add(new Label
        {
            Text = "A few more things",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = true, Left = 40, Top = 18,
        });
        p.Controls.Add(new Label
        {
            Text = "These can all be changed later in Settings.",
            Font = new Font("Segoe UI", 10f), ForeColor = TextMuted,
            AutoSize = true, Left = 40, Top = 56,
        });

        var cbStartup = MakeToggle(p, "Launch iRPC when Windows starts",
            "Start automatically when you log in - no need to open it manually.",
            false, 108);

        var cbUpdates = MakeToggle(p, "Check for updates automatically",
            "Get notified when a new version is available each time you start iRPC.",
            true, 188);

        var cbGitHub = MakeToggle(p, "Show GitHub button on your presence",
            "Adds a button to your Discord presence linking to the iRPC GitHub page.",
            true, 268,
            note: "Keeping it on helps spread the word and support the project. It means a lot!");

        return (p, cbStartup, cbUpdates, cbGitHub);
    }

    private static CheckBox MakeToggle(Panel p, string label, string description, bool defaultOn, int top, string? note = null)
    {
        var cb = new CheckBox
        {
            Text = label, Left = 40, Top = top,
            AutoSize = true, Checked = defaultOn, ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 10f),
        };
        p.Controls.Add(cb);
        p.Controls.Add(new Label
        {
            Text = description, Left = 62, Top = top + 26,
            Width = 420, Height = 18, ForeColor = TextMuted,
            Font = new Font("Segoe UI", 9f), AutoSize = false,
        });
        if (note is not null)
            p.Controls.Add(new Label
            {
                Text = note, Left = 62, Top = top + 44,
                Width = 420, Height = 18, ForeColor = TextMuted,
                Font = new Font("Segoe UI", 9f, FontStyle.Italic), AutoSize = false,
            });
        return cb;
    }

    private static Panel StepPanel() =>
        new() { Left = 0, Top = 0, Width = 540, Height = 368, BackColor = BgForm };

    private void UpdatePreview()
    {
        string name = _presetList.SelectedItem?.ToString() ?? "";
        if (!AppSettings.DefaultPresets.TryGetValue(name, out var preset)) return;
        var cfg = preset.SessionTemplates.TryGetValue("Race", out var r) ? r
            : preset.SessionTemplates.Values.FirstOrDefault() ?? new SessionPresenceConfig();
        _previewDetails.Text = DiscordService.ApplyTemplate(cfg.DetailsTemplate, PreviewData);
        _previewState.Text   = DiscordService.ApplyTemplate(cfg.StateTemplate,   PreviewData);
        _presetDesc.Text     = Descriptions.TryGetValue(name, out var d) ? d : "";
    }

    private void ShowStep(int step)
    {
        _step            = step;
        _step0.Visible   = step == 0;
        _step1.Visible   = step == 1;
        _step2.Visible   = step == 2;
        _btnBack.Visible = step > 0;
        _btnNext.Text    = step == 2 ? "Finish" : "Next";
    }

    private void OnNext(object? sender, EventArgs e)
    {
        if (_step < 2) { ShowStep(_step + 1); return; }
        ApplyAll();
        Close();
    }

    private void ApplyAll()
    {
        ApplyPreset();
        _settings.LaunchOnStartup          = _cbStartup.Checked;
        _settings.CheckForUpdatesOnStartup = _cbUpdates.Checked;
        _settings.ShowGitHubButton         = _cbGitHub.Checked;
    }

    private void ApplyPreset()
    {
        string name = _presetList.SelectedItem?.ToString() ?? "";
        if (!AppSettings.DefaultPresets.TryGetValue(name, out var preset)) return;
        foreach (var kv in preset.SessionTemplates)
            _settings.SessionTemplates[kv.Key] = kv.Value;
        _settings.LargeTextTemplate = preset.LargeTextTemplate;
        _settings.SmallTextTemplate = preset.SmallTextTemplate;
    }
}

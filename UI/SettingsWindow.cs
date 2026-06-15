using System.Drawing;
using System.Windows.Forms;

namespace iRPC;

public class SettingsWindow : Form
{
    public AppSettings Settings { get; private set; }

    private readonly TextBox _appIdBox;
    private readonly ComboBox _cmbLargeIcon;
    private readonly ComboBox _cmbSmallIcon;
    private readonly CheckBox _cbCarName;
    private readonly CheckBox _cbLapProgress;
    private readonly CheckBox _cbPosition;
    private readonly CheckBox _cbTimeRemaining;
    private readonly CheckBox _cbFlag;
    private readonly CheckBox _cbElapsedTimer;
    private readonly CheckBox _cbLaunchOnStartup;
    private readonly CheckBox _cbShowGitHubButton;
    private readonly Action<AppSettings> _onSave;

    public SettingsWindow(AppSettings current, Action<AppSettings> onSave)
    {
        _onSave = onSave;
        Settings = current;

        Text = "iRPC Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;

        int x = 12, y = 12;

        // Discord App ID
        Controls.Add(new Label { Text = "Discord App ID:", Left = x, Top = y, AutoSize = true });
        y += 18;
        _appIdBox = new TextBox { Left = x, Top = y, Width = 280, Text = current.DiscordAppId };
        Controls.Add(_appIdBox);
        y += 30;

        // Large icon
        _cmbLargeIcon = MakeComboBox("Large icon:", x, ref y,
            ["iRacing logo", "iRPC logo", "Track logo"],
            (int)current.LargeIcon);

        // Small icon
        _cmbSmallIcon = MakeComboBox("Small icon:", x, ref y,
            ["Off", "Car brand", "Session type"],
            (int)current.SmallIcon);

        y += 4;

        // Presence toggles
        _cbCarName             = MakeCheckbox("Show car name",                         current.ShowCarName,             x, ref y);
        _cbLapProgress         = MakeCheckbox("Show lap progress",                     current.ShowLapProgress,         x, ref y);
        _cbPosition            = MakeCheckbox("Show position (race only)",             current.ShowPosition,            x, ref y);
        _cbTimeRemaining       = MakeCheckbox("Show time remaining",                   current.ShowTimeRemaining,       x, ref y);
        _cbFlag                = MakeCheckbox("Show caution/checkered flag indicator", current.ShowFlag,             x, ref y);
        _cbElapsedTimer        = MakeCheckbox("Show elapsed session timer",            current.ShowElapsedTimer,     x, ref y);
        _cbLaunchOnStartup     = MakeCheckbox("Launch on Windows startup",             current.LaunchOnStartup,      x, ref y);
        _cbShowGitHubButton    = MakeCheckbox("Show GitHub button",                   current.ShowGitHubButton,     x, ref y);

        y += 8;

        var btnSave  = new Button { Text = "Save",  Left = 140, Top = y, Width = 75 };
        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Left = 220, Top = y, Width = 75 };
        Controls.Add(btnSave);
        Controls.Add(btnClose);
        btnSave.Click += OnSave;

        ClientSize = new Size(308, y + btnSave.Height + 12);
        CancelButton = btnClose;
    }

    private ComboBox MakeComboBox(string label, int x, ref int y, string[] items, int selectedIndex)
    {
        Controls.Add(new Label { Text = label, Left = x, Top = y, AutoSize = true });
        y += 18;
        var cmb = new ComboBox
        {
            Left = x, Top = y, Width = 180,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        cmb.Items.AddRange(items);
        cmb.SelectedIndex = selectedIndex;
        Controls.Add(cmb);
        y += cmb.Height + 8;
        return cmb;
    }

    private CheckBox MakeCheckbox(string text, bool value, int x, ref int y)
    {
        var cb = new CheckBox { Text = text, Left = x, Top = y, AutoSize = true, Checked = value };
        Controls.Add(cb);
        y += cb.PreferredSize.Height + 2;
        return cb;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        Settings = new AppSettings
        {
            DiscordAppId      = _appIdBox.Text.Trim(),
            LargeIcon         = (LargeIconMode)_cmbLargeIcon.SelectedIndex,
            SmallIcon         = (SmallIconMode)_cmbSmallIcon.SelectedIndex,
            ShowCarName       = _cbCarName.Checked,
            ShowLapProgress   = _cbLapProgress.Checked,
            ShowPosition      = _cbPosition.Checked,
            ShowTimeRemaining = _cbTimeRemaining.Checked,
            ShowFlag          = _cbFlag.Checked,
            ShowElapsedTimer  = _cbElapsedTimer.Checked,
            LaunchOnStartup   = _cbLaunchOnStartup.Checked,
            ShowGitHubButton  = _cbShowGitHubButton.Checked,
        };
        Settings.Save();
        _onSave(Settings);
    }
}

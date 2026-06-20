using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;

namespace iRPC;

// Lets the user view/edit %AppData%\iRPC\key_overrides.json without leaving the app,
// and validate that each override actually resolves to an image hosted in ArtAssets on GitHub.
public class KeyOverridesDialog : Form
{
    private static readonly Color BgForm    = Color.FromArgb(43, 45, 49);
    private static readonly Color BgInput   = Color.FromArgb(24, 25, 28);
    private static readonly Color BgAccent  = Color.FromArgb(88, 101, 242);
    private static readonly Color BgClose   = Color.FromArgb(64, 66, 73);
    private static readonly Color TextPrimary = Color.FromArgb(219, 222, 225);
    private static readonly Color TextMuted   = Color.FromArgb(148, 155, 164);
    private static readonly Color RedMissing  = Color.FromArgb(237, 66, 69);
    private static readonly Color GreenOk     = Color.FromArgb(87, 242, 135);

    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly Button _btnValidate;

    public KeyOverridesDialog()
    {
        Text = "Key Overrides";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize  = new Size(480, 440);
        BackColor   = BgForm;

        var hint = new Label
        {
            Text = "Maps an auto-generated asset key to the actual ArtAssets filename (without .png).",
            Left = 14, Top = 10, Width = 452, Height = 32,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5f),
        };
        Controls.Add(hint);

        _grid = new DataGridView
        {
            Left = 14, Top = 46, Width = 452, Height = 300,
            BackgroundColor = BgInput, BorderStyle = BorderStyle.None,
            AllowUserToResizeRows = false, RowHeadersVisible = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            GridColor = Color.FromArgb(60, 62, 68),
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = BgClose;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
        _grid.DefaultCellStyle.BackColor = BgInput;
        _grid.DefaultCellStyle.ForeColor = TextPrimary;
        _grid.DefaultCellStyle.SelectionBackColor = BgAccent;
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Key", Width = 200 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Override value", Width = 230 });

        foreach (var kv in KeyOverrides.GetAll().OrderBy(k => k.Key))
            _grid.Rows.Add(kv.Key, kv.Value);

        Controls.Add(_grid);

        var btnAdd = new Button
        {
            Text = "Add Row", Left = 14, Top = 354, Width = 90, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btnAdd.FlatAppearance.BorderSize = 0;
        btnAdd.Click += (_, _) => _grid.Rows.Add("", "");

        var btnRemove = new Button
        {
            Text = "Remove Row", Left = 110, Top = 354, Width = 100, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btnRemove.FlatAppearance.BorderSize = 0;
        btnRemove.Click += (_, _) =>
        {
            foreach (DataGridViewRow row in _grid.SelectedRows)
                if (!row.IsNewRow) _grid.Rows.Remove(row);
        };

        _btnValidate = new Button
        {
            Text = "Validate", Left = 350, Top = 354, Width = 116, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        _btnValidate.FlatAppearance.BorderSize = 0;
        _btnValidate.Click += OnValidate;

        Controls.AddRange([btnAdd, btnRemove, _btnValidate]);

        _status = new Label
        {
            Left = 14, Top = 386, Width = 452, Height = 18,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5f),
        };
        Controls.Add(_status);

        var btnSave  = MakeButton("Save", BgAccent, 270, 412);
        var btnClose = MakeButton("Close", BgClose, 358, 412);
        btnClose.DialogResult = DialogResult.Cancel;
        btnSave.Click += OnSave;
        Controls.AddRange([btnSave, btnClose]);
        CancelButton = btnClose;
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var map = new Dictionary<string, string>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string key = row.Cells["Key"].Value?.ToString()?.Trim() ?? "";
            string val = row.Cells["Value"].Value?.ToString()?.Trim() ?? "";
            if (key.Length == 0 || val.Length == 0) continue;
            map[key] = val;
        }
        KeyOverrides.SetAll(map);
        DialogResult = DialogResult.OK;
        Close();
    }

    private async void OnValidate(object? sender, EventArgs e)
    {
        _btnValidate.Enabled = false;
        _status.Text = "Validating…";

        using var http = new HttpClient();
        int missing = 0, checked_ = 0, skipped = 0;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string key = row.Cells["Key"].Value?.ToString()?.Trim() ?? "";
            string val = row.Cells["Value"].Value?.ToString()?.Trim() ?? "";
            if (key.Length == 0 || val.Length == 0) continue;

            string? url = key switch
            {
                _ when key.StartsWith("track_") => $"{DiscordService.AssetBase}/Tracks/{val}.png",
                _ when key.StartsWith("brand_")  => $"{DiscordService.AssetBase}/Brands/{val}.png",
                _ => null,
            };

            if (url is null)
            {
                row.Cells["Value"].Style.BackColor = BgInput;
                skipped++;
                continue;
            }

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, url);
                using var resp = await http.SendAsync(req);
                bool ok = resp.IsSuccessStatusCode;
                row.Cells["Value"].Style.BackColor = ok ? Color.FromArgb(28, 46, 33) : Color.FromArgb(54, 26, 27);
                row.Cells["Value"].Style.ForeColor = ok ? GreenOk : RedMissing;
                if (!ok) missing++;
            }
            catch
            {
                row.Cells["Value"].Style.BackColor = Color.FromArgb(54, 26, 27);
                row.Cells["Value"].Style.ForeColor = RedMissing;
                missing++;
            }
            checked_++;
        }

        _status.Text = $"Checked {checked_}, {missing} missing" + (skipped > 0 ? $", {skipped} skipped (unknown prefix)" : "");
        _btnValidate.Enabled = true;
    }

    private static Button MakeButton(string text, Color bg, int left, int top)
    {
        var btn = new Button
        {
            Text = text, Left = left, Top = top, Width = 76, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}

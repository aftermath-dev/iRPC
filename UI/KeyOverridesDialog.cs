using System.Drawing;
using System.Net.Http;
using System.Windows.Forms;

namespace iRPC;

// Lets the user view/edit %AppData%\iRPC\key_overrides.json without leaving the app.
// Built-in brand and track mappings are handled automatically — entries here override them.
//
// Brands tab: car_{codename} → brand_{name}  (e.g. car_camarozl12018 → brand_chevrolet)
//             brand_{word}   → brand_{name}  (first-word fallback for unlisted cars)
// Tracks tab: track_{key}   → track_{name}  (e.g. track_watkins_glen_boot → track_watkins_glen)
public class KeyOverridesDialog : Form
{
    private static readonly Color BgForm      = Color.FromArgb(43, 45, 49);
    private static readonly Color BgInput     = Color.FromArgb(24, 25, 28);
    private static readonly Color BgAccent    = Color.FromArgb(88, 101, 242);
    private static readonly Color BgClose     = Color.FromArgb(64, 66, 73);
    private static readonly Color BgTab       = Color.FromArgb(35, 36, 40);
    private static readonly Color TextPrimary = Color.FromArgb(219, 222, 225);
    private static readonly Color TextMuted   = Color.FromArgb(148, 155, 164);
    private static readonly Color GreenOk     = Color.FromArgb(87, 242, 135);
    private static readonly Color RedMissing  = Color.FromArgb(237, 66, 69);

    private readonly DataGridView _brandGrid;
    private readonly DataGridView _trackGrid;
    private readonly TabControl   _tabs;
    private readonly Label        _status;
    private readonly Button       _btnValidate;

    public KeyOverridesDialog()
    {
        Text            = "Key Overrides";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new Size(520, 480);
        BackColor       = BgForm;

        _tabs = new TabControl
        {
            Left = 10, Top = 10, Width = 500, Height = 370,
            DrawMode   = TabDrawMode.OwnerDrawFixed,
            ItemSize   = new Size(120, 26),
            SizeMode   = TabSizeMode.Fixed,
            Appearance = TabAppearance.Normal,
            Font       = new Font("Segoe UI", 9f),
        };
        _tabs.DrawItem += OnDrawTab;
        Controls.Add(_tabs);

        _brandGrid = MakeGrid();
        _trackGrid = MakeGrid();

        var brandHint = MakeHint(
            "Override brand logos by car codename (car_{folder}) or display-name word (brand_{word}).\n" +
            "Value = asset filename without .png,  e.g.  car_camarozl12018  →  brand_chevrolet");

        var trackHint = MakeHint(
            "Override track logo filenames. Value = asset filename without .png.\n" +
            "e.g.  track_watkins_glen_boot  →  track_watkins_glen");

        _tabs.TabPages.Add(MakePage("Brands", brandHint, _brandGrid));
        _tabs.TabPages.Add(MakePage("Tracks", trackHint, _trackGrid));

        // Populate from user overrides only; built-ins apply silently via KeyOverrides.Apply()
        foreach (var kv in KeyOverrides.GetAll().OrderBy(k => k.Key))
        {
            if (kv.Key.StartsWith("track_", StringComparison.OrdinalIgnoreCase))
                _trackGrid.Rows.Add(kv.Key, kv.Value);
            else
                _brandGrid.Rows.Add(kv.Key, kv.Value);
        }

        // ── Per-tab Add / Remove buttons (toggled on tab switch) ─────
        var btnAddBrand    = MakeSmallButton("Add Row",    14,  390);
        var btnRemoveBrand = MakeSmallButton("Remove Row", 110, 390);
        var btnAddTrack    = MakeSmallButton("Add Row",    14,  390);
        var btnRemoveTrack = MakeSmallButton("Remove Row", 110, 390);

        btnAddBrand.Click    += (_, _) => _brandGrid.Rows.Add("car_", "brand_");
        btnRemoveBrand.Click += (_, _) => RemoveSelected(_brandGrid);
        btnAddTrack.Click    += (_, _) => _trackGrid.Rows.Add("track_", "");
        btnRemoveTrack.Click += (_, _) => RemoveSelected(_trackGrid);

        void SyncTabButtons()
        {
            bool brands = _tabs.SelectedIndex == 0;
            btnAddBrand.Visible    = brands;
            btnRemoveBrand.Visible = brands;
            btnAddTrack.Visible    = !brands;
            btnRemoveTrack.Visible = !brands;
        }
        _tabs.SelectedIndexChanged += (_, _) => SyncTabButtons();
        SyncTabButtons();

        _btnValidate = MakeSmallButton("Validate", 370, 390);
        _btnValidate.Width  = 130;
        _btnValidate.Click += OnValidate;

        _status = new Label
        {
            Left = 14, Top = 424, Width = 492, Height = 18,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5f),
        };

        var btnSave  = MakeButton("Save",  BgAccent, 304, 450);
        var btnClose = MakeButton("Close", BgClose,  392, 450);
        btnClose.DialogResult = DialogResult.Cancel;
        btnSave.Click += OnSave;

        Controls.AddRange([btnAddBrand, btnRemoveBrand, btnAddTrack, btnRemoveTrack,
                           _btnValidate, _status, btnSave, btnClose]);
        CancelButton = btnClose;
    }

    // ── Tab drawing ──────────────────────────────────────────────

    private void OnDrawTab(object? sender, DrawItemEventArgs e)
    {
        bool selected = e.Index == _tabs.SelectedIndex;
        using var bg = new SolidBrush(selected ? BgForm : BgTab);
        e.Graphics.FillRectangle(bg, e.Bounds);
        using var fg = new SolidBrush(selected ? TextPrimary : TextMuted);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(_tabs.TabPages[e.Index].Text, _tabs.Font, fg, e.Bounds, sf);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static TabPage MakePage(string title, Label hint, DataGridView grid)
    {
        var page = new TabPage(title) { BackColor = BgForm, Padding = new Padding(0) };
        hint.Left = 6; hint.Top = 6;
        grid.Left = 6; grid.Top = hint.Bottom + 4;
        grid.Width = 486; grid.Height = 330 - hint.Height - 10;
        page.Controls.AddRange([hint, grid]);
        return page;
    }

    private static Label MakeHint(string text) => new()
    {
        Text = text, Width = 486, Height = 36, AutoSize = false,
        ForeColor = TextMuted, Font = new Font("Segoe UI", 8.5f),
    };

    private static DataGridView MakeGrid()
    {
        var g = new DataGridView
        {
            BackgroundColor = BgInput, BorderStyle = BorderStyle.None,
            AllowUserToResizeRows = false, RowHeadersVisible = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles  = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            GridColor     = Color.FromArgb(60, 62, 68),
        };
        g.ColumnHeadersDefaultCellStyle.BackColor = BgClose;
        g.ColumnHeadersDefaultCellStyle.ForeColor = TextPrimary;
        g.DefaultCellStyle.BackColor = BgInput;
        g.DefaultCellStyle.ForeColor = TextPrimary;
        g.DefaultCellStyle.SelectionBackColor = BgAccent;
        g.DefaultCellStyle.SelectionForeColor = Color.White;
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key",   HeaderText = "Key",            Width = 240 });
        g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Override value", Width = 224 });
        return g;
    }

    private static void RemoveSelected(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.SelectedRows)
            if (!row.IsNewRow) grid.Rows.Remove(row);
    }

    private static Button MakeSmallButton(string text, int left, int top)
    {
        var b = new Button
        {
            Text = text, Left = left, Top = top, Width = 90, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private static Button MakeButton(string text, Color bg, int left, int top)
    {
        var b = new Button
        {
            Text = text, Left = left, Top = top, Width = 80, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    // ── Logic ────────────────────────────────────────────────────

    private void OnSave(object? sender, EventArgs e)
    {
        var map = new Dictionary<string, string>();
        CollectGrid(_brandGrid, map);
        CollectGrid(_trackGrid, map);
        KeyOverrides.SetAll(map);
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void CollectGrid(DataGridView grid, Dictionary<string, string> map)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            string key = row.Cells["Key"].Value?.ToString()?.Trim() ?? "";
            string val = row.Cells["Value"].Value?.ToString()?.Trim() ?? "";
            if (key.Length > 0 && val.Length > 0) map[key] = val;
        }
    }

    private async void OnValidate(object? sender, EventArgs e)
    {
        _btnValidate.Enabled = false;
        _status.Text = "Validating…";

        var activeGrid = _tabs.SelectedIndex == 0 ? _brandGrid : _trackGrid;

        using var http = new HttpClient();
        int missing = 0, checked_ = 0, skipped = 0;

        foreach (DataGridViewRow row in activeGrid.Rows)
        {
            if (row.IsNewRow) continue;
            string key = row.Cells["Key"].Value?.ToString()?.Trim() ?? "";
            string val = row.Cells["Value"].Value?.ToString()?.Trim() ?? "";
            if (key.Length == 0 || val.Length == 0) continue;

            string? url = key switch
            {
                _ when key.StartsWith("track_", StringComparison.OrdinalIgnoreCase)
                    => $"{DiscordService.AssetBase}/Tracks/{val}.png",
                _ when key.StartsWith("car_",   StringComparison.OrdinalIgnoreCase) ||
                       key.StartsWith("brand_", StringComparison.OrdinalIgnoreCase)
                    => $"{DiscordService.AssetBase}/Brands/{val}.png",
                _ => null,
            };

            if (url is null) { skipped++; row.Cells["Value"].Style.BackColor = BgInput; continue; }

            try
            {
                using var req  = new HttpRequestMessage(HttpMethod.Head, url);
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

        _status.Text = $"Checked {checked_}, {missing} missing"
                     + (skipped > 0 ? $", {skipped} skipped (unknown prefix)" : "");
        _btnValidate.Enabled = true;
    }
}

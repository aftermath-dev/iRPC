using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace iRPC;

// Compact alternative to BrickRow: draggable chips that wrap to multiple rows,
// [+Add] dropdown, and separator picker. Height is dynamic based on content.
public class ChipTemplateEditor : Control, ITemplateEditor
{
    private static readonly (string Group, string[] Keys)[] Groups =
    [
        ("Session",      ["session", "track", "config", "car"]),
        ("Position",     ["position", "class_position", "lap", "laps_total", "laps_remain", "time_remain", "flag"]),
        ("Lap Times",    ["last_lap", "best_lap"]),
        ("Rating",       ["sof", "irating", "irating_avg5", "irating_avg10", "irating_avg_custom"]),
        ("Speed && Fuel", ["speed_kmh", "speed_mph", "fuel", "fuel_gal", "fuel_pct"]),
        ("Weather",       ["sky", "air_temp_c", "air_temp_f", "track_temp_c", "track_temp_f"]),
        ("Pit && Status", ["pit_service", "pit_repair", "pit_opt_repair", "fast_repairs", "pit", "garage", "incidents"]),
    ];

    private static readonly (string Sep, string Label)[] Seps =
    [
        (" | ", "| pipe"),
        (" - ", "- dash"),
        (" · ", "· dot"),
        (" / ", "/ slash"),
        (", ",  ", comma"),
    ];

    private static readonly Color ChipBg  = Color.FromArgb(64, 66, 73);
    private static readonly Color ChipFg  = Color.FromArgb(219, 222, 225);
    private static readonly Color BtnBg   = Color.FromArgb(50, 52, 58);
    private static readonly Color BtnFg   = Color.FromArgb(148, 155, 164);
    private static readonly Color ClsHov  = Color.FromArgb(235, 80, 80);
    private static readonly Color ClsMut  = Color.FromArgb(140, 142, 148);
    private static readonly Color DropClr = Color.FromArgb(88, 101, 242);
    private static readonly Font  BFont   = new("Segoe UI", 8.5f);

    private const int H    = 24;
    private const int R    = 4;
    private const int PadX = 8;
    private const int ClsW = 14;
    private const int Gap  = 4;

    private readonly List<string> _active = [];
    private readonly Dictionary<string, int> _wCache = [];
    private string _sep;

    // Rebuilt by ComputeLayout() on every content/size change.
    private readonly record struct ChipEntry(int Index, Rectangle Rect);
    private List<ChipEntry> _chipLayout = [];
    private Rectangle _addRect, _sepRect;

    private int   _dragIdx = -1, _dropIdx = -1;
    private bool  _dragging;
    private Point _dragOrigin;
    private int   _hoverIdx = -1;
    private bool  _hoverClose;
    private int   _hoverBtn = -1; // 0=Add 1=Sep

    public event Action? Changed;

    public ChipTemplateEditor(string defaultSep = " | ")
    {
        _sep = defaultSep;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = H + 4;
    }

    // ── ITemplateEditor ───────────────────────────────────────────

    public string GetTemplate() =>
        _active.Count == 0 ? "" : string.Join(_sep, _active.Select(k => $"{{{k}}}"));

    public void SetFromTemplate(string template)
    {
        _active.Clear();
        foreach (Match m in Regex.Matches(template, @"\{(\w+)\}"))
        {
            string key = m.Groups[1].Value;
            if (BrickRow.All.Any(b => b.Key == key) && !_active.Contains(key))
                _active.Add(key);
        }
        if (_active.Count >= 2)
        {
            var sm = Regex.Match(template, @"\}(.+?)\{");
            if (sm.Success && Seps.Any(s => s.Sep == sm.Groups[1].Value))
                _sep = sm.Groups[1].Value;
        }
        ComputeLayout();
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ComputeLayout();
        Invalidate();
    }

    // ── Layout ────────────────────────────────────────────────────

    private void ComputeLayout()
    {
        _chipLayout = [];
        int cx = 0, cy = 0;
        int maxW = Math.Max(Width, 100);

        for (int i = 0; i < _active.Count; i++)
        {
            int w = ChipW(_active[i]);
            if (cx > 0 && cx + w > maxW) { cx = 0; cy += H + Gap; }
            _chipLayout.Add(new ChipEntry(i, new Rectangle(cx, cy, w, H)));
            cx += w + Gap;
        }

        int aw = Measure("+ Add") + PadX * 2;
        if (cx > 0 && cx + aw > maxW) { cx = 0; cy += H + Gap; }
        _addRect = new Rectangle(cx, cy, aw, H);
        cx += aw + Gap;

        string sl = SepBtnLabel;
        int sw = Measure(sl) + PadX * 2;
        if (cx + sw > maxW) { cx = 0; cy += H + Gap; }
        _sepRect = new Rectangle(cx, cy, sw, H);

        Height = cy + H + 4;
    }

    // ── Paint ─────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        for (int i = 0; i <= _active.Count; i++)
        {
            if (_dragging && _dropIdx == i && !(i == _dragIdx || i == _dragIdx + 1))
            {
                var dropR = i < _chipLayout.Count ? _chipLayout[i].Rect : _addRect;
                using var pen = new Pen(DropClr, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, dropR.X - 2, dropR.Y + 2, dropR.X - 2, dropR.Y + H - 2);
            }
            if (i == _active.Count) break;

            var entry = _chipLayout[i];
            bool dimmed = _dragging && i == _dragIdx;
            bool cls    = _hoverIdx == i && _hoverClose;
            if (!dimmed) DrawChip(g, entry.Rect, LabelOf(_active[i]), cls);
        }

        DrawPill(g, _addRect, "+ Add", _hoverBtn == 0);
        DrawPill(g, _sepRect, SepBtnLabel, _hoverBtn == 1);
    }

    private void DrawChip(Graphics g, Rectangle r, string label, bool closeHov)
    {
        using var path = RoundRect(r, R);
        g.FillPath(new SolidBrush(ChipBg), path);
        g.DrawString(label, BFont, new SolidBrush(ChipFg), r.X + PadX, r.Y + 4);
        g.DrawString("×", BFont, new SolidBrush(closeHov ? ClsHov : ClsMut), r.Right - ClsW - 2, r.Y + 4);
    }

    private void DrawPill(Graphics g, Rectangle r, string text, bool hov)
    {
        using var path = RoundRect(r, R);
        g.FillPath(new SolidBrush(hov ? Color.FromArgb(62, 64, 72) : BtnBg), path);
        g.DrawString(text, BFont, new SolidBrush(BtnFg), r.X + PadX, r.Y + 4);
    }

    // ── Mouse ────────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragIdx >= 0 && !_dragging && Math.Abs(e.X - _dragOrigin.X) > 4)
        {
            _dragging = true;
            Cursor = Cursors.SizeWE;
        }
        if (_dragging)
        {
            int d = DropAt(e.X, e.Y);
            if (d != _dropIdx) { _dropIdx = d; Invalidate(); }
            return;
        }
        var (idx, cls, btn) = HitTest(e.X, e.Y);
        bool changed = idx != _hoverIdx || cls != _hoverClose || btn != _hoverBtn;
        _hoverIdx = idx; _hoverClose = cls; _hoverBtn = btn;
        if (changed) Invalidate();
        Cursor = idx >= 0 && !cls && btn < 0 ? Cursors.SizeWE : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var (idx, cls, btn) = HitTest(e.X, e.Y);
        if (btn == 0) { ShowAddMenu(); return; }
        if (btn == 1) { ShowSepMenu(); return; }
        if (idx >= 0)
        {
            if (cls)
            {
                _active.RemoveAt(idx);
                _hoverIdx = -1;
                Changed?.Invoke();
                ComputeLayout();
                Invalidate();
            }
            else
            {
                _dragIdx = idx; _dropIdx = idx; _dragOrigin = e.Location; Capture = true;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragIdx < 0) return;
        if (_dragging && _dropIdx != _dragIdx && _dropIdx != _dragIdx + 1)
        {
            string item = _active[_dragIdx];
            _active.RemoveAt(_dragIdx);
            int ins = _dropIdx > _dragIdx ? _dropIdx - 1 : _dropIdx;
            _active.Insert(Math.Clamp(ins, 0, _active.Count), item);
            Changed?.Invoke();
            ComputeLayout();
        }
        _dragging = false; _dragIdx = -1; _dropIdx = -1;
        Capture = false; Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIdx = -1; _hoverClose = false; _hoverBtn = -1;
        Invalidate();
    }

    // ── Menus ─────────────────────────────────────────────────────

    private void ShowAddMenu()
    {
        var menu = MakeMenu();
        bool any = false;

        foreach (var (group, keys) in Groups)
        {
            var available = keys
                .Select(k => BrickRow.All.FirstOrDefault(b => b.Key == k))
                .Where(d => d is not null && !_active.Contains(d.Key))
                .ToList();
            if (available.Count == 0) continue;

            any = true;
            var groupItem = new ToolStripMenuItem(group) { ForeColor = Color.FromArgb(219, 222, 225) };

            foreach (var def in available)
            {
                string key = def!.Key;
                var item = new ToolStripMenuItem(def.Label)
                {
                    ForeColor   = Color.FromArgb(219, 222, 225),
                    ToolTipText = def.Tip,
                };
                item.Click += (_, _) => { _active.Add(key); Changed?.Invoke(); ComputeLayout(); Invalidate(); };
                groupItem.DropDownItems.Add(item);
            }

            menu.Items.Add(groupItem);
        }

        if (!any)
            menu.Items.Add(new ToolStripMenuItem("(all added)") { Enabled = false, ForeColor = Color.FromArgb(148, 155, 164) });

        menu.Show(this, new Point(_addRect.Left, _addRect.Bottom + 2));
    }

    private void ShowSepMenu()
    {
        var menu = MakeMenu();
        foreach (var (sep, label) in Seps)
        {
            string s = sep;
            var item = new ToolStripMenuItem(label) { Checked = sep == _sep, ForeColor = Color.FromArgb(219, 222, 225) };
            item.Click += (_, _) =>
            {
                _sep = s;
                ComputeLayout();
                Changed?.Invoke();
                Invalidate();
            };
            menu.Items.Add(item);
        }
        menu.Show(this, new Point(_sepRect.Left, _sepRect.Bottom + 2));
    }

    private static ContextMenuStrip MakeMenu() => new()
    {
        BackColor      = Color.FromArgb(32, 33, 36),
        Renderer       = new DarkMenuRenderer(),
        ShowItemToolTips = true,
    };

    // ── Helpers ──────────────────────────────────────────────────

    private (int idx, bool cls, int btn) HitTest(int mx, int my)
    {
        if (_addRect.Contains(mx, my)) return (-1, false, 0);
        if (_sepRect.Contains(mx, my)) return (-1, false, 1);
        foreach (var entry in _chipLayout)
        {
            if (entry.Rect.Contains(mx, my))
                return (entry.Index, mx >= entry.Rect.Right - ClsW - 2, -1);
        }
        return (-1, false, -1);
    }

    private int DropAt(int mx, int my)
    {
        foreach (var entry in _chipLayout)
        {
            if (entry.Index == _dragIdx) continue;
            var r = entry.Rect;
            if (my < r.Bottom && mx < r.X + r.Width / 2) return entry.Index;
        }
        return _active.Count;
    }

    private string SepBtnLabel =>
        (Seps.FirstOrDefault(s => s.Sep == _sep).Label ?? _sep.Trim()) + " ▾";

    private int ChipW(string key)
    {
        if (_wCache.TryGetValue(key, out int v)) return v;
        return _wCache[key] = Measure(LabelOf(key)) + PadX * 2 + ClsW + 2;
    }

    private static int Measure(string text)
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        return (int)Math.Ceiling(g.MeasureString(text, BFont).Width);
    }

    private static string LabelOf(string key) =>
        BrickRow.All.FirstOrDefault(b => b.Key == key)?.Label ?? key;

    private static GraphicsPath RoundRect(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
        p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
        p.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
        p.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(148, 155, 164);
            base.OnRenderArrow(e);
        }
    }

    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground   => Color.FromArgb(32, 33, 36);
        public override Color ImageMarginGradientBegin      => Color.FromArgb(32, 33, 36);
        public override Color ImageMarginGradientMiddle     => Color.FromArgb(32, 33, 36);
        public override Color ImageMarginGradientEnd        => Color.FromArgb(32, 33, 36);
        public override Color MenuItemSelected              => Color.FromArgb(64, 66, 73);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(64, 66, 73);
        public override Color MenuItemSelectedGradientEnd   => Color.FromArgb(64, 66, 73);
        public override Color MenuItemBorder                => Color.Transparent;
        public override Color MenuBorder                    => Color.FromArgb(54, 56, 62);
        public override Color SeparatorDark                 => Color.FromArgb(60, 62, 68);
        public override Color SeparatorLight                => Color.FromArgb(60, 62, 68);
    }
}

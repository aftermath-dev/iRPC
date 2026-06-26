using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace iRPC;

public interface ITemplateEditor
{
    string GetTemplate();
    void SetFromTemplate(string template);
    event Action? Changed;
}

public record BrickDef(string Key, string Label, string Tip);

// Horizontal brick row with drag-to-reorder + clickable pool below it.
public class BrickRow : Control, ITemplateEditor
{
    public static readonly BrickDef[] All =
    [
        new("session",     "Session",    "Session type (Race, Practice, Qualify...)"),
        new("track",       "Track",      "Track name"),
        new("config",      "Config",     "Track configuration (Full, Boot...)"),
        new("car",         "Car",        "Car name"),
        new("position",    "Position",   "Current position — P1, P2... (empty outside race)"),
        new("lap",         "Lap",        "Current lap number"),
        new("laps_total",  "Lap Total",  "Lap progress (e.g. Lap 12/20)"),
        new("laps_remain", "Laps Left",  "Laps remaining (e.g. 8 laps left)"),
        new("time_remain", "Time",       "Time remaining (e.g. 45:23)"),
        new("last_lap",    "Last Lap",   "Last lap time (e.g. LL 1:32.456)"),
        new("best_lap",    "Best Lap",   "Best lap time this session (e.g. BL 1:31.987)"),
        new("sof",         "SoF",        "Strength of field — iRating-based (e.g. SoF 2450)"),
        new("irating",          "iRating",        "Your current iRating (e.g. iR 2450)"),
        new("irating_avg5",     "iRating Avg 5",  "Average of your last 5 race-end iRatings (e.g. iR Avg5 2410)"),
        new("irating_avg10",    "iRating Avg 10", "Average of your last 10 race-end iRatings (e.g. iR Avg10 2390)"),
        new("irating_avg_custom", "iRating Avg (Custom)", "Average over the custom window set in App settings (e.g. iR Avg 2375)"),
        new("class_position", "Class Pos",   "Position within your car class — multiclass races (e.g. P2 in class)"),
        new("sky",             "Sky",         "Current sky condition (e.g. Partly Cloudy)"),
        new("air_temp_c",      "Air °C",      "Air temperature in Celsius (e.g. Air 24°C)"),
        new("air_temp_f",      "Air °F",      "Air temperature in Fahrenheit (e.g. Air 75°F)"),
        new("track_temp_c",    "Track °C",    "Track surface temperature in Celsius (e.g. Track 32°C)"),
        new("track_temp_f",    "Track °F",    "Track surface temperature in Fahrenheit (e.g. Track 90°F)"),
        new("pit_service",     "Pit Service", "Currently being serviced in the pits (empty otherwise)"),
        new("pit_repair",      "Pit Repair",  "Mandatory damage repair time remaining (e.g. Repair 12s)"),
        new("pit_opt_repair",  "Pit Opt Repair", "Optional/cosmetic repair time remaining (e.g. Opt Repair 8s)"),
        new("fast_repairs",    "Fast Repairs", "Fast repairs used/available this race (e.g. FR 1/3)"),
        new("incidents",       "Incidents",   "Incident points this session (e.g. 3x)"),
        new("speed_kmh",   "km/h",       "Current speed in km/h"),
        new("speed_mph",   "mph",        "Current speed in mph"),
        new("fuel",        "Fuel L",     "Fuel level in litres (e.g. F 45.2L)"),
        new("fuel_gal",    "Fuel gal",   "Fuel level in US gallons (e.g. F 11.9gal)"),
        new("fuel_pct",    "Fuel %",     "Fuel percentage (e.g. F 60%)"),
        new("flag",        "Flag",       "Caution or Checkered (empty otherwise)"),
        new("pit",         "Pit",        "In Pits (empty otherwise)"),
        new("garage",      "Garage",     "In Garage (empty otherwise)"),
    ];

    // Visual constants
    private static readonly Color ActiveBg   = Color.FromArgb(64,  66,  73);
    private static readonly Color ActiveFg   = Color.FromArgb(219, 222, 225);
    private static readonly Color PoolBg     = Color.FromArgb(36,  37,  41);
    private static readonly Color PoolFg     = Color.FromArgb(120, 124, 132);
    private static readonly Color DropColor  = Color.FromArgb(88,  101, 242);
    private static readonly Color MutedFg    = Color.FromArgb(100, 103, 110);
    private static readonly Font  BFont      = new("Segoe UI", 8.5f);
    private const int H     = 24;   // brick height
    private const int R     = 4;    // corner radius
    private const int PadX  = 8;    // inner horizontal padding
    private const int ClsW  = 14;   // width of × area
    private const int Gap   = 4;    // gap between bricks
    private const int ActiveY = 0;
    private const int PoolLabelY = 32;
    private const int PoolY = 46;

    private readonly List<string> _active = [];
    private readonly string _sep;
    private readonly ToolTip _tip = new() { InitialDelay = 300 };
    private readonly Dictionary<string, int> _wCache = [];

    // Drag state
    private int   _dragIdx = -1;
    private int   _dropIdx = -1;
    private bool  _dragging;
    private Point _dragOrigin;

    // Hover state
    private int  _hoverActiveIdx = -1;
    private bool _hoverClose;
    private string? _hoverPoolKey;

    public event Action? Changed;

    public BrickRow(string separator)
    {
        _sep = separator;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalcHeight();
    }

    // ── Public API ────────────────────────────────────────────────

    public string GetTemplate() =>
        _active.Count == 0 ? "" : string.Join(_sep, _active.Select(k => $"{{{k}}}"));

    public void SetFromTemplate(string template)
    {
        _active.Clear();
        foreach (Match m in Regex.Matches(template, @"\{(\w+)\}"))
        {
            string key = m.Groups[1].Value;
            if (All.Any(b => b.Key == key) && !_active.Contains(key))
                _active.Add(key);
        }
        RecalcHeight();
        Invalidate();
    }

    // ── Painting ──────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Active row
        int x = 0;
        for (int i = 0; i <= _active.Count; i++)
        {
            if (_dragging && _dropIdx == i && !(i == _dragIdx || i == _dragIdx + 1))
            {
                using var pen = new Pen(DropColor, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(pen, x - 2, ActiveY + 2, x - 2, ActiveY + H - 2);
            }
            if (i == _active.Count) break;

            bool isDragged = _dragging && i == _dragIdx;
            int w = W(_active[i]);
            bool closeHover = _hoverActiveIdx == i && _hoverClose;
            DrawBrick(g, x, ActiveY, w, LabelOf(_active[i]), true, isDragged, closeHover);
            x += w + Gap;
        }

        // "Available" label
        using var mutedBrush = new SolidBrush(MutedFg);
        g.DrawString("Available", new Font("Segoe UI", 7.5f), mutedBrush, 0f, (float)PoolLabelY);

        // Pool row (wraps)
        x = 0; int py = PoolY;
        foreach (var def in All)
        {
            if (_active.Contains(def.Key)) continue;
            int w = W(def.Key);
            if (x > 0 && x + w > WrapWidth) { x = 0; py += H + Gap; }
            bool hov = _hoverPoolKey == def.Key;
            DrawBrick(g, x, py, w, def.Label, false, false, false, hov);
            x += w + Gap;
        }
    }

    private void DrawBrick(Graphics g, int x, int y, int w, string label,
        bool active, bool dimmed, bool closeHover, bool poolHover = false)
    {
        var bg = active
            ? (dimmed ? Color.FromArgb(45, 47, 52) : ActiveBg)
            : (poolHover ? Color.FromArgb(50, 52, 58) : PoolBg);
        var fg = active ? (dimmed ? MutedFg : ActiveFg) : PoolFg;

        var rect = new Rectangle(x, y, w, H);
        using var path = RoundRect(rect, R);
        g.FillPath(new SolidBrush(bg), path);

        g.DrawString(label, BFont, new SolidBrush(fg), x + PadX, y + 4);

        if (active)
        {
            var xFg = closeHover ? Color.FromArgb(235, 80, 80) : Color.FromArgb(140, 142, 148);
            g.DrawString("×", BFont, new SolidBrush(xFg), x + w - ClsW - 2, y + 4);
        }
    }

    // ── Mouse ────────────────────────────────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        // Start drag after threshold
        if (_dragIdx >= 0 && !_dragging)
        {
            if (Math.Abs(e.X - _dragOrigin.X) > 4)
            {
                _dragging = true;
                Cursor = Cursors.SizeWE;
            }
        }

        if (_dragging)
        {
            int d = DropAt(e.X);
            if (d != _dropIdx) { _dropIdx = d; Invalidate(); }
            return;
        }

        // Hover detection
        var (ai, cls, pk) = HitTest(e.X, e.Y);
        bool changed = ai != _hoverActiveIdx || cls != _hoverClose || pk != _hoverPoolKey;
        _hoverActiveIdx = ai; _hoverClose = cls; _hoverPoolKey = pk;
        if (changed) Invalidate();

        // Tooltip
        string? tipKey = ai >= 0 ? _active[ai] : pk;
        var def = tipKey != null ? All.FirstOrDefault(b => b.Key == tipKey) : null;
        if (def != null)
            _tip.Show(def.Tip, this, e.X + 16, e.Y, 3000);
        else
            _tip.Hide(this);

        Cursor = (ai >= 0 && !cls) ? Cursors.SizeWE : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var (ai, cls, pk) = HitTest(e.X, e.Y);

        if (pk != null)
        {
            _active.Add(pk);
            Changed?.Invoke();
            RecalcHeight();
            Invalidate();
            return;
        }

        if (ai >= 0)
        {
            if (cls)
            {
                _active.RemoveAt(ai);
                _hoverActiveIdx = -1;
                Changed?.Invoke();
                RecalcHeight();
                Invalidate();
            }
            else
            {
                _dragIdx = ai;
                _dropIdx = ai;
                _dragOrigin = e.Location;
                Capture = true;
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
        }

        _dragging = false;
        _dragIdx  = -1;
        _dropIdx  = -1;
        Capture   = false;
        Cursor    = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverActiveIdx = -1; _hoverClose = false; _hoverPoolKey = null;
        Invalidate();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private (int activeIdx, bool isClose, string? poolKey) HitTest(int mx, int my)
    {
        if (my >= ActiveY && my < ActiveY + H)
        {
            int x = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                int w = W(_active[i]);
                if (mx >= x && mx < x + w)
                    return (i, mx >= x + w - ClsW - 2, null);
                x += w + Gap;
            }
        }

        if (my >= PoolY)
        {
            int x = 0, py = PoolY;
            foreach (var def in All)
            {
                if (_active.Contains(def.Key)) continue;
                int w = W(def.Key);
                if (x > 0 && x + w > WrapWidth) { x = 0; py += H + Gap; }
                if (my >= py && my < py + H && mx >= x && mx < x + w)
                    return (-1, false, def.Key);
                x += w + Gap;
            }
        }

        return (-1, false, null);
    }

    private int DropAt(int mx)
    {
        int x = 0;
        for (int i = 0; i < _active.Count; i++)
        {
            if (i == _dragIdx) { x += W(_active[i]) + Gap; continue; }
            int w = W(_active[i]);
            if (mx < x + w / 2) return i;
            x += w + Gap;
        }
        return _active.Count;
    }

    private int WrapWidth => Math.Max(Width, 400);

    // Worst-case height for a BrickRow at the given width: every brick sitting unassigned in
    // the pool (none active), which is the tallest the control can ever wrap to. Callers that
    // need to reserve fixed layout space below a BrickRow (so the control beneath it doesn't
    // shift as bricks move between active/pool) should size against this instead of guessing.
    public static int MaxHeight(int width)
    {
        int wrap = Math.Max(width, 400);
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        int x = 0, rows = 1;
        foreach (var def in All)
        {
            int w = (int)Math.Ceiling(g.MeasureString(def.Label, BFont).Width) + PadX * 2 + ClsW + 2;
            if (x > 0 && x + w > wrap) { rows++; x = 0; }
            x += w + Gap;
        }
        return PoolY + rows * (H + Gap) + 4;
    }

    private void RecalcHeight()
    {
        int x = 0, rows = 1;
        foreach (var def in All)
        {
            if (_active.Contains(def.Key)) continue;
            int w = W(def.Key);
            if (x > 0 && x + w > WrapWidth) { rows++; x = 0; }
            x += w + Gap;
        }
        Height = PoolY + rows * (H + Gap) + 4;
    }

    private int W(string key)
    {
        if (_wCache.TryGetValue(key, out int cached)) return cached;
        string label = LabelOf(key);
        using var bmp = new Bitmap(1, 1);
        using var g   = Graphics.FromImage(bmp);
        int w = (int)Math.Ceiling(g.MeasureString(label, BFont).Width) + PadX * 2 + ClsW + 2;
        _wCache[key] = w;
        return w;
    }

    private static string LabelOf(string key) =>
        All.FirstOrDefault(b => b.Key == key)?.Label ?? key;

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
}

using System.Drawing;
using System.Windows.Forms;

namespace iRPC;

// Dark-themed dropdown: draws as a styled panel, opens a ContextMenuStrip on click.
sealed class DarkDropDown : Control
{
    private static readonly Color Bg     = Color.FromArgb(24, 25, 28);
    private static readonly Color Fg     = Color.FromArgb(219, 222, 225);
    private static readonly Color Border = Color.FromArgb(60, 62, 68);
    private static readonly Color Muted  = Color.FromArgb(148, 155, 164);
    private static readonly Font  F      = new("Segoe UI", 9f);

    private string[] _items;
    private readonly ContextMenuStrip _menu;
    private int _sel;

    public int SelectedIndex
    {
        get => _sel;
        set { _sel = Math.Clamp(value, 0, _items.Length - 1); Invalidate(); }
    }

    public string? SelectedItem => _sel >= 0 && _sel < _items.Length ? _items[_sel] : null;

    public event EventHandler? SelectedIndexChanged;

    public void SetItems(string[] items, int selected = 0)
    {
        _items = items;
        _menu.Items.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var mi = new ToolStripMenuItem(items[i]) { ForeColor = Fg };
            mi.Click += (_, _) => { _sel = idx; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); };
            _menu.Items.Add(mi);
        }
        _sel = items.Length > 0 ? Math.Clamp(selected, 0, items.Length - 1) : -1;
        Invalidate();
    }

    public DarkDropDown(string[] items, int selected = 0)
    {
        _items = items;
        Height = 23;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);

        _menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(32, 33, 36),
            Renderer  = new ToolStripProfessionalRenderer(new DarkColors()),
        };

        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var mi = new ToolStripMenuItem(items[i]) { ForeColor = Fg };
            mi.Click += (_, _) =>
            {
                _sel = idx;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            };
            _menu.Items.Add(mi);
        }

        _sel = Math.Clamp(selected, 0, items.Length - 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _menu.MinimumSize = new Size(Width, 0);
        _menu.Show(this, 0, Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Bg);
        using var pen = new Pen(Border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        string text = _sel >= 0 && _sel < _items.Length ? _items[_sel] : "";
        g.DrawString(text, F, new SolidBrush(Fg), 6f, 4f);
        g.DrawString("▾",  F, new SolidBrush(Muted), Width - 18f, 4f);
    }

    private sealed class DarkColors : ProfessionalColorTable
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

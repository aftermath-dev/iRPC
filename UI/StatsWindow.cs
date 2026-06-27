using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace iRPC;

public class StatsWindow : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);
    private const int WM_SETREDRAW = 11;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int v = 1;
        DwmSetWindowAttribute(Handle, 20, ref v, sizeof(int));
    }

    private static readonly Color BgForm      = Color.FromArgb(43, 45, 49);
    private static readonly Color BgClose     = Color.FromArgb(64, 66, 73);
    private static readonly Color BgDivider   = Color.FromArgb(60, 62, 68);
    private static readonly Color TextPrimary = Color.FromArgb(219, 222, 225);
    private static readonly Color TextMuted   = Color.FromArgb(148, 155, 164);
    private static readonly Color BgAccent    = Color.FromArgb(88, 101, 242);

    private static readonly string[] SessionOrder =
        ["Practice", "Qualify", "Race", "Test Drive", "Time Trial"];

    // Stats only change while iRPC's own poll loop is running (same process), so rather than
    // watching the file we just re-snapshot from StatsTracker on a timer while the window is open.
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Panel _content;
    private ulong _lastRenderedTotalSeconds = ulong.MaxValue;

    public StatsWindow(StatsData stats)
    {
        Text = "iRPC Stats";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new Size(420, 680);
        BackColor       = BgForm;

        _content = new Panel { Left = 0, Top = 0, Width = 420, Height = 640, AutoScroll = true, BackColor = BgForm };
        Controls.Add(_content);
        Render(stats);
        _lastRenderedTotalSeconds = stats.TotalSeconds;

        var btnClose = new Button
        {
            Text = "Close", Left = 320, Top = 644, Width = 80, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) =>
        {
            var snap = StatsTracker.Snapshot();
            if (snap.TotalSeconds == _lastRenderedTotalSeconds) return;
            _lastRenderedTotalSeconds = snap.TotalSeconds;
            Render(snap);
        };
        _refreshTimer.Start();
        FormClosed += (_, _) => _refreshTimer.Dispose();
    }

    private void Render(StatsData stats)
    {
        // Controls.Clear() + rebuild repaints as each control is added/removed, which flickers
        // visibly on a 1s timer. Suspending WM_SETREDRAW defers all of that to a single repaint
        // once everything is back in place.
        bool redrawSuppressed = _content.IsHandleCreated;
        if (redrawSuppressed) SendMessage(_content.Handle, WM_SETREDRAW, false, 0);

        _content.SuspendLayout();
        var old = _content.Controls.Cast<Control>().ToArray();
        _content.Controls.Clear();
        foreach (var c in old) c.Dispose();

        int x = 20, y = 16;

        Header("Total Time On Track", x, ref y);
        Value(FormatDuration(stats.TotalSeconds), x, ref y, big: true);
        Row("Laps",      $"{stats.TotalLaps:N0}", x, ref y);
        Row("Distance",  FormatDistance(stats.TotalDistanceM), x, ref y);
        Row("Incidents", $"{stats.TotalIncidents:N0}", x, ref y);
        y += 10;

        Divider(x, ref y);
        Header("By Session Type", x, ref y);
        if (stats.SecondsBySessionType.Count == 0)
            Muted("No data yet — get on track first.", x, ref y);
        else
            foreach (string key in SessionOrder.Concat(
                         stats.SecondsBySessionType.Keys.Except(SessionOrder).OrderBy(k => k)))
            {
                ulong secs = stats.SecondsBySessionType.GetValueOrDefault(key);
                if (secs == 0) continue;
                Row(key, FormatDuration(secs), x, ref y);
            }

        y += 6;
        Divider(x, ref y);
        Header("Laps by Track", x, ref y);
        TopLapsList(stats.LapsByTrack, x, ref y);

        y += 6;
        Divider(x, ref y);
        Header("Favorite Cars", x, ref y);
        TopList(stats.SecondsByCar, x, ref y);

        y += 6;
        Divider(x, ref y);
        Header("Favorite Tracks", x, ref y);
        TopList(stats.SecondsByTrack, x, ref y);

        _content.ResumeLayout();

        if (redrawSuppressed)
        {
            SendMessage(_content.Handle, WM_SETREDRAW, true, 0);
            _content.Invalidate(true);
            _content.Refresh();
        }
    }

    private void TopList(Dictionary<string, ulong> map, int x, ref int y)
    {
        if (map.Count == 0) { Muted("No data yet.", x, ref y); return; }
        foreach (var kv in map.OrderByDescending(kv => kv.Value).Take(5))
            Row(kv.Key, FormatDuration(kv.Value), x, ref y);
    }

    private void TopLapsList(Dictionary<string, ulong> map, int x, ref int y)
    {
        if (map.Count == 0) { Muted("No data yet.", x, ref y); return; }
        foreach (var kv in map.OrderByDescending(kv => kv.Value).Take(5))
            Row(kv.Key, $"{kv.Value:N0} laps", x, ref y);
    }

    private void Header(string text, int x, ref int y)
    {
        _content.Controls.Add(new Label
        {
            Text = text, Left = x, Top = y, AutoSize = true,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        });
        y += 24;
    }

    private void Value(string text, int x, ref int y, bool big = false)
    {
        _content.Controls.Add(new Label
        {
            Text = text, Left = x, Top = y, AutoSize = true,
            ForeColor = TextPrimary, Font = new Font("Segoe UI", big ? 20f : 10f, big ? FontStyle.Bold : FontStyle.Regular),
        });
        y += big ? 36 : 22;
    }

    private void Row(string label, string value, int x, ref int y)
    {
        _content.Controls.Add(new Label
        {
            Text = label, Left = x, Top = y, Width = 260, AutoSize = false,
            ForeColor = TextPrimary, Font = new Font("Segoe UI", 9.5f),
        });
        _content.Controls.Add(new Label
        {
            Text = value, Left = x + 260, Top = y, Width = 120, AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 9.5f),
        });
        y += 22;
    }

    private void Muted(string text, int x, ref int y)
    {
        _content.Controls.Add(new Label
        {
            Text = text, Left = x, Top = y, AutoSize = true,
            ForeColor = TextMuted, Font = new Font("Segoe UI", 9f, FontStyle.Italic),
        });
        y += 22;
    }

    private void Divider(int x, ref int y)
    {
        _content.Controls.Add(new Panel { Left = x, Top = y, Width = 380, Height = 1, BackColor = BgDivider });
        y += 12;
    }

    private static string FormatDuration(ulong totalSeconds)
    {
        const ulong maxSecs = 922_337_203_685;
        var ts = TimeSpan.FromSeconds(Math.Min(totalSeconds, maxSecs));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static string FormatDistance(ulong metres)
    {
        double km = metres / 1000.0;
        return km >= 1000 ? $"{km / 1000:N2} Mm" : $"{km:N1} km";
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace iRPC;

public class UpdateProgressDialog : Form
{
    private static readonly Color BgForm = Color.FromArgb(43, 45, 49);
    private static readonly Color TextPrimary = Color.FromArgb(219, 222, 225);
    private static readonly Color TextMuted = Color.FromArgb(148, 155, 164);
    private static readonly Color BgClose = Color.FromArgb(64, 66, 73);

    private readonly ProgressBar _bar;
    private readonly Label _status;
    private readonly Button _btnCancel;

    public CancellationTokenSource Cts { get; } = new();

    public UpdateProgressDialog(string version)
    {
        Text = "iRPC Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ClientSize = new Size(340, 122);
        BackColor = BgForm;

        Controls.Add(new Label
        {
            Text = $"Downloading v{version}...",
            Left = 16, Top = 14, AutoSize = true, ForeColor = TextPrimary,
        });

        _bar = new ProgressBar { Left = 16, Top = 42, Width = 308, Height = 20, Minimum = 0, Maximum = 100 };
        Controls.Add(_bar);

        _status = new Label { Left = 16, Top = 68, AutoSize = true, ForeColor = TextMuted };
        Controls.Add(_status);

        _btnCancel = new Button
        {
            Text = "Cancel", Left = 249, Top = 86, Width = 75, Height = 26,
            FlatStyle = FlatStyle.Flat, BackColor = BgClose, ForeColor = Color.White,
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        _btnCancel.Click += (_, _) =>
        {
            Cts.Cancel();
            _btnCancel.Enabled = false;
            _btnCancel.Text = "Cancelling...";
        };
        Controls.Add(_btnCancel);
    }

    public void ReportProgress(long bytesReceived, long? totalBytes)
    {
        if (IsDisposed) return;

        if (totalBytes is > 0)
        {
            _bar.Style = ProgressBarStyle.Continuous;
            _bar.Value = (int)Math.Clamp(bytesReceived * 100 / totalBytes.Value, 0, 100);
            _status.Text = $"{bytesReceived / 1_048_576.0:F1} MB / {totalBytes.Value / 1_048_576.0:F1} MB";
        }
        else
        {
            _bar.Style = ProgressBarStyle.Marquee;
            _status.Text = $"{bytesReceived / 1_048_576.0:F1} MB downloaded";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Cts.Dispose();
        base.Dispose(disposing);
    }
}

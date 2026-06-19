using System.Windows.Forms;

namespace iRPC;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, "iRPC-SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("iRPC is already running in the system tray.", "iRPC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        Application.ThreadException += (_, e) => LogUnhandled(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogUnhandled(e.ExceptionObject as Exception, fatal: true);

        Application.Run(new TrayApp());
    }

    // Force-enable logging on crash so we get a trace even if the user never turned on debug mode.
    // Only AppDomain.UnhandledException is actually fatal — Application.ThreadException lets the
    // message loop keep running, so it doesn't warrant a "last run crashed" notice on next launch.
    private static void LogUnhandled(Exception? ex, bool fatal)
    {
        if (ex is null) return;
        Logger.Enabled = true;
        Logger.Log($"Unhandled exception (fatal={fatal}): {ex}");
        if (fatal) Logger.MarkCrashed();
    }
}

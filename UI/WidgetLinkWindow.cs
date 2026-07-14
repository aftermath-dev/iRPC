using System.Drawing;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace iRPC;

public class WidgetLinkWindow : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int v = 1;
        DwmSetWindowAttribute(Handle, 20, ref v, sizeof(int));
    }

    private static readonly Color BgForm     = Color.FromArgb(43, 45, 49);
    private static readonly Color BgAccent   = Color.FromArgb(88, 101, 242);
    private static readonly Color BgClose    = Color.FromArgb(64, 66, 73);
    private static readonly Color TextPrimary = Color.FromArgb(219, 222, 225);
    private static readonly Color TextMuted   = Color.FromArgb(148, 155, 164);
    private static readonly Color GreenOk     = Color.FromArgb(87, 242, 135);
    private static readonly Color RedErr      = Color.FromArgb(240, 71, 71);

    private const int ListenPort = 7423;

    private readonly string _appId;
    // _clientSecret kept for future use but not needed for implicit grant
    private readonly Action<string, string, long, string> _onLinked;

    private readonly Panel _stepIntro;
    private readonly Panel _stepWaiting;
    private readonly Panel _stepDone;

    private readonly Label _doneIcon;
    private readonly Label _doneTitle;
    private readonly Label _doneBody;
    private readonly Button _btnDone;
    private readonly Button _btnTryAgain;

    private CancellationTokenSource? _cts;

    public WidgetLinkWindow(string appId, string clientSecret,
        Action<string, string, long, string> onLinked)
    {
        _appId    = appId;
        _onLinked = onLinked;

        Text            = "Link Discord Account";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ClientSize      = new Size(480, 280);
        BackColor       = BgForm;

        // ── Step 1: Intro ─────────────────────────────────────
        _stepIntro = Step();

        var introTitle = L("Link Discord Account", 20, 20, 440, TextPrimary,
            new Font("Segoe UI", 11f, FontStyle.Bold));
        var introBody = L(
            "This connects your Discord account so iRPC can push live iRacing stats to your profile widget.\n\n" +
            "Your browser will open and ask you to authorize iRPC.\n" +
            "Required scopes: openid  •  sdk.social_layer",
            20, 56, 440, TextMuted, new Font("Segoe UI", 9f));
        introBody.Height = 70; introBody.AutoSize = false;
        var introHint = L(
            $"Required: add  http://localhost:{ListenPort}/callback  as a Redirect URI in\nDeveloper Portal → OAuth2 before clicking Link.",
            20, 138, 440, Color.FromArgb(250, 166, 26), new Font("Segoe UI", 8.5f));
        introHint.Height = 34; introHint.AutoSize = false;

        var btnCancelIntro = Btn("Cancel", BgClose, 384, 236);
        var btnStart       = Btn("Link Account →", BgAccent, 258, 236);
        btnStart.Width = 118;
        btnCancelIntro.Click += (_, _) => Close();
        btnStart.Click += OnStart;
        _stepIntro.Controls.AddRange([introTitle, introBody, introHint, btnStart, btnCancelIntro]);

        // ── Step 2: Waiting ───────────────────────────────────
        _stepWaiting = Step();
        _stepWaiting.Visible = false;

        var waitTitle = L("Waiting for Discord authorization...", 20, 20, 440, TextPrimary,
            new Font("Segoe UI", 11f, FontStyle.Bold));
        var waitBody = L(
            "Your browser should have opened Discord's authorization page.\n" +
            "Once you authorize, iRPC will automatically continue.",
            20, 60, 440, TextMuted, new Font("Segoe UI", 9f));
        waitBody.Height = 40; waitBody.AutoSize = false;
        var waitHint = L($"Listening on http://localhost:{ListenPort}/callback",
            20, 112, 440, Color.FromArgb(110, 115, 125), new Font("Segoe UI", 8f));

        var btnReopen     = Btn("Open Browser Again", BgClose, 20, 236);
        btnReopen.Width   = 148;
        var btnCancelWait = Btn("Cancel", BgClose, 384, 236);
        btnReopen.Click     += (_, _) => OpenBrowser();
        btnCancelWait.Click += (_, _) => { _cts?.Cancel(); ShowStep(_stepIntro); };
        _stepWaiting.Controls.AddRange([waitTitle, waitBody, waitHint, btnReopen, btnCancelWait]);

        // ── Step 3: Done ──────────────────────────────────────
        _stepDone = Step();
        _stepDone.Visible = false;

        _doneIcon  = L("", 20, 16, 48, GreenOk, new Font("Segoe UI", 24f));
        _doneTitle = L("", 72, 22, 388, TextPrimary, new Font("Segoe UI", 11f, FontStyle.Bold));
        _doneBody  = L("", 20, 76, 440, TextMuted, new Font("Segoe UI", 9f));
        _doneBody.Height = 120; _doneBody.AutoSize = false;

        _btnDone     = Btn("Done", BgAccent, 384, 236);
        _btnTryAgain = Btn("Try Again", BgClose, 20, 236);
        var btnCancelDone = Btn("Cancel", BgClose, 384, 236);
        _btnDone.Click      += (_, _) => Close();
        _btnTryAgain.Click  += (_, _) => ShowStep(_stepIntro);
        btnCancelDone.Click += (_, _) => Close();
        _stepDone.Controls.AddRange([_doneIcon, _doneTitle, _doneBody, _btnDone, _btnTryAgain, btnCancelDone]);

        Controls.AddRange([_stepIntro, _stepWaiting, _stepDone]);
    }

    private async void OnStart(object? sender, EventArgs e)
    {
        ShowStep(_stepWaiting);
        OpenBrowser();
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var (token, expiresIn) = await WaitForTokenAsync(_cts.Token);
            await FetchUserAndCompleteAsync(token, expiresIn, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !(_cts?.IsCancellationRequested ?? true))
                ShowResult(false, "Timed out", "No response within 5 minutes. Click Try Again to restart.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                ShowResult(false, "Linking failed", ex.Message);
        }
    }

    private void OpenBrowser()
    {
        string redirectUri = Uri.EscapeDataString($"http://localhost:{ListenPort}/callback");
        string scope       = Uri.EscapeDataString("openid sdk.social_layer");
        string url = $"https://discord.com/api/oauth2/authorize" +
                     $"?client_id={_appId}" +
                     $"&response_type=token" +
                     $"&redirect_uri={redirectUri}" +
                     $"&scope={scope}";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = url, UseShellExecute = true });
    }

    // Implicit grant returns the token in the URL fragment (#access_token=...).
    // Browsers strip the fragment before sending the request to the server, so we
    // serve a tiny JS page that reads the fragment and fires a second GET to /token.
    private static async Task<(string token, int expiresIn)> WaitForTokenAsync(CancellationToken ct)
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, ListenPort);
        listener.Start();
        using var reg = ct.Register(() => listener.Stop());

        // Request 1: browser hits /callback — serve JS bridge page
        var client1 = await listener.AcceptTcpClientAsync(ct);
        using (client1)
        {
            using var s = client1.GetStream();
            var buf = new byte[4096];
            await s.ReadAsync(buf, 0, buf.Length, ct);

            string html =
                "<html><body style='background:#2b2d31;color:#dbdee1;font-family:Segoe UI;" +
                "display:flex;align-items:center;justify-content:center;height:100vh;margin:0'>" +
                "<h2 id='m'>Authorizing...</h2><script>" +
                "var p=new URLSearchParams(location.hash.slice(1));" +
                "var t=p.get('access_token'),e=p.get('expires_in')||'604800';" +
                "if(t){var i=new Image();" +
                $"i.src='http://localhost:{ListenPort}/token?t='+encodeURIComponent(t)+'&e='+e;" +
                "i.onload=i.onerror=function(){document.getElementById('m').textContent=" +
                "'✓ Authorized! You can close this tab.';document.getElementById('m').style.color='#57f287';};" +
                "}else{document.getElementById('m').textContent='Authorization failed. Return to iRPC.';" +
                "document.getElementById('m').style.color='#f04747';}" +
                "</script></body></html>";
            byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
            byte[] header    = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {htmlBytes.Length}\r\nConnection: close\r\n\r\n");
            await s.WriteAsync(header, ct);
            await s.WriteAsync(htmlBytes, ct);
        }

        // Request 2: JS fires GET /token?t=TOKEN&e=EXPIRES_IN
        var client2 = await listener.AcceptTcpClientAsync(ct);
        using (client2)
        {
            using var s = client2.GetStream();
            var buf = new byte[8192];
            int n = await s.ReadAsync(buf, 0, buf.Length, ct);
            string req      = Encoding.ASCII.GetString(buf, 0, n);
            string firstLine = req.Split('\r', '\n')[0];
            string[] parts   = firstLine.Split(' ');
            string pathAndQuery = parts.Length > 1 ? parts[1] : "";
            int q = pathAndQuery.IndexOf('?');
            string query = q >= 0 ? pathAndQuery[(q + 1)..] : "";

            string? token     = QueryParam(query, "t");
            string? expiresStr = QueryParam(query, "e");

            byte[] ok = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await s.WriteAsync(ok, ct);

            if (string.IsNullOrWhiteSpace(token))
                throw new Exception("No token received from browser.");

            int expiresIn = int.TryParse(expiresStr, out int ei) ? ei : 604800;
            return (token, expiresIn);
        }
    }

    private async Task FetchUserAndCompleteAsync(string token, int expiresIn, CancellationToken ct)
    {
        long expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn;

        using var http = new System.Net.Http.HttpClient();
        using var req  = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Get, "https://discord.com/api/oauth2/@me");
        req.Headers.Add("Authorization", $"Bearer {token}");
        using var resp = await http.SendAsync(req, ct);
        string json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Couldn't fetch user info ({(int)resp.StatusCode}): {json}");

        using var doc = JsonDocument.Parse(json);
        var user       = doc.RootElement.GetProperty("user");
        string? userId     = user.TryGetProperty("id",          out var id) ? id.GetString() : null;
        string? username   = user.TryGetProperty("username",    out var un) ? un.GetString() : null;
        string? globalName = user.TryGetProperty("global_name", out var gn) ? gn.GetString() : null;

        if (userId is null) throw new Exception("Couldn't read Discord user ID.");
        string displayName = globalName ?? username ?? userId;

        Invoke(() =>
        {
            if (IsDisposed) return;
            _onLinked(token, userId, expiry, displayName);
            ShowResult(true, "Account linked!",
                $"Linked as:  {displayName}\n\n" +
                "iRPC will push live stats to your Discord profile widget every 30 seconds while you're in a session.\n\n" +
                "Note: this token expires in 7 days. Re-link when prompted.");
        });
    }

    private void ShowResult(bool success, string title, string body)
    {
        _doneIcon.Text      = success ? "✓" : "✗";
        _doneIcon.ForeColor = success ? GreenOk : RedErr;
        _doneTitle.Text     = title;
        _doneBody.Text      = body;
        _btnDone.Visible     = success;
        _btnTryAgain.Visible = !success;
        ShowStep(_stepDone);
    }

    private void ShowStep(Panel step)
    {
        foreach (Control c in Controls)
            if (c is Panel p) p.Visible = p == step;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnFormClosed(e);
    }

    private static string? QueryParam(string query, string key)
    {
        foreach (string part in query.Split('&'))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == key)
                return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
        }
        return null;
    }

    private Panel Step()
    {
        return new Panel { Left = 0, Top = 0, Width = 480, Height = 280, BackColor = BgForm };
    }

    private static Label L(string text, int x, int y, int w, Color color, Font font)
    {
        return new Label { Text = text, Left = x, Top = y, Width = w, ForeColor = color, Font = font, AutoSize = true };
    }

    private static Button Btn(string text, Color bg, int x, int y)
    {
        var btn = new Button
        {
            Text = text, Left = x, Top = y, Width = 88, Height = 28,
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f), Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }
}

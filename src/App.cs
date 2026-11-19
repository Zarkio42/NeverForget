// NeverForget — main window, tray icon and the bridge to the local web UI.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace NeverForget
{
    static class Program
    {
        public const string AppName = "NeverForget";
        public const string AppId = "NeverForget";

        [STAThread]
        static void Main(string[] args)
        {
            string a0 = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (a0 == "--install") { Shortcuts.CreateDesktop(); return; }
            if (a0 == "--uninstall") { Shortcuts.RemoveAll(); return; }

            bool created;
            using (var mutex = new Mutex(true, "Local\\" + AppId + ".Instance", out created))
            using (var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + AppId + ".Show"))
            using (var quitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + AppId + ".Quit"))
            {
                if (!created)
                {
                    // Another instance is running: ask it to show itself (or to exit, with --quit).
                    if (a0 == "--quit")
                    {
                        quitEvt.Set();
                        try { if (mutex.WaitOne(8000)) mutex.ReleaseMutex(); } catch (AbandonedMutexException) { }
                    }
                    else showEvt.Set();
                    return;
                }
                if (a0 == "--quit") return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => Log(e.Exception);

                bool startHidden = a0 == "--tray";
                var form = new MainForm(startHidden);

                var waiter = new Thread(() =>
                {
                    while (true)
                    {
                        int which = WaitHandle.WaitAny(new WaitHandle[] { showEvt, quitEvt });
                        try { form.BeginInvoke(new Action(which == 0 ? (Action)form.ShowFromTray : form.ExitApp)); } catch { return; }
                    }
                });
                waiter.IsBackground = true;
                waiter.Start();

                Application.Run(form);
                GC.KeepAlive(mutex);
            }
        }

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppId);
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static void Log(Exception ex)
        {
            try
            {
                string path = Path.Combine(DataDir, "errors.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 1024 * 1024) fi.Delete(); // keep the log small
                File.AppendAllText(path, DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine);
            }
            catch { }
        }
    }

    /// <summary>Tray/dialog strings. The web UI has its own translations (web/app.js).</summary>
    static class Strings
    {
        public static string Lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt" : "en";

        static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            //                       English                                                    Português
            { "open",      new[] { "Open",                                                    "Abrir" } },
            { "newNote",   new[] { "New note",                                                "Nova nota" } },
            { "autostart", new[] { "Start with Windows",                                      "Iniciar com o Windows" } },
            { "exit",      new[] { "Exit",                                                    "Sair" } },
            { "trayHint",  new[] { "Still running in the tray. Click the icon or press Ctrl+Alt+N to come back.",
                                   "Continuo aberto na bandeja. Clique no ícone ou use Ctrl+Alt+N para voltar." } },
            { "webError",  new[] { "Could not start the interface component (Microsoft Edge WebView2).",
                                   "Não foi possível iniciar o componente de interface (Microsoft Edge WebView2)." } },
        };

        public static string T(string key) { return Map[key][Lang == "pt" ? 1 : 0]; }
    }

    public sealed class MainForm : Form
    {
        // Reserved .example name: never resolves on the internet, mapped to the local web folder.
        const string Host = "neverforget.example";
        const string Origin = "https://" + Host + "/";

        readonly WebView2 _web = new WebView2();
        readonly NotifyIcon _tray = new NotifyIcon();
        // Input from the page is capped; replies (e.g. the full note list) are not.
        readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        readonly JavaScriptSerializer _out = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly System.Windows.Forms.Timer _ruleTimer = new System.Windows.Forms.Timer();
        readonly Db _db;
        readonly Store _store;
        readonly bool _startHidden;
        bool _exiting, _hintShown, _ready;
        string _theme = "system"; // "system" follows Windows; "light" / "dark" force it
        ToolStripMenuItem _openItem, _newItem, _autoItem, _exitItem;
        const int HOTKEY_ID = 0x4E46;

        public MainForm(bool startHidden)
        {
            _startHidden = startHidden;
            _db = new Db(Path.Combine(Program.DataDir, "neverforget.db"));
            _store = new Store(_db);
            string lang = _store.GetSetting("lang");
            if (lang == "en" || lang == "pt") Strings.Lang = lang;
            _theme = NormalizeTheme(_store.GetSetting("theme"));

            Text = Program.AppName;
            Icon = AppIcon();
            MinimumSize = new Size(420, 480);
            StartPosition = FormStartPosition.Manual;
            LoadBounds();

            _web.Dock = DockStyle.Fill;
            ApplyBackColor();
            Controls.Add(_web);

            BuildTray();

            if (_startHidden) { ShowInTaskbar = false; WindowState = FormWindowState.Minimized; }

            Load += async (s, e) =>
            {
                if (_startHidden) { Hide(); ShowInTaskbar = true; WindowState = FormWindowState.Normal; }
                ApplyTitleBarTheme();
                RegisterHotKey(Handle, HOTKEY_ID, 0x0001 | 0x0002 | 0x4000, (uint)Keys.N); // Ctrl+Alt+N, no repeat
                try { await InitWebAsync(); }
                catch (Exception ex)
                {
                    Program.Log(ex);
                    MessageBox.Show(this, Strings.T("webError") + "\n\n" + ex.Message,
                        Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            SystemEvents.UserPreferenceChanged += OnUserPrefChanged;

            // Automations run when the UI is ready and then every 30 minutes.
            _ruleTimer.Interval = 30 * 60 * 1000;
            _ruleTimer.Tick += OnRuleTimer;
            _ruleTimer.Start();
        }

        // ---------- WebView2 ----------

        async System.Threading.Tasks.Task InitWebAsync()
        {
            string webDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDir, "WebView2"), null);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;

            bool dev = Array.IndexOf(Environment.GetCommandLineArgs(), "--dev") > 0;
            var st = core.Settings;
            st.AreDevToolsEnabled = dev;
            st.AreBrowserAcceleratorKeysEnabled = dev;
            st.AreHostObjectsAllowed = false;      // the bridge is postMessage only
            st.IsStatusBarEnabled = false;
            st.IsZoomControlEnabled = false;
            st.IsGeneralAutofillEnabled = false;
            st.IsPasswordAutosaveEnabled = false;
            st.IsReputationCheckingRequired = false; // no SmartScreen lookups: the page is local and never navigates

            // Local content only: the UI can never navigate, open windows, download or ask for permissions.
            core.SetVirtualHostNameToFolderMapping(Host, webDir, CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (s, e) => { if (!IsOurs(e.Uri)) e.Cancel = true; };
            core.FrameNavigationStarting += (s, e) => { if (!IsOurs(e.Uri)) e.Cancel = true; };
            core.NewWindowRequested += (s, e) => { e.Handled = true; };
            core.DownloadStarting += (s, e) => { e.Cancel = true; };
            core.PermissionRequested += (s, e) => { e.State = CoreWebView2PermissionState.Deny; };
            core.WebMessageReceived += OnWebMessage;
            ApplyWebColorScheme(); // before the first paint, so the page never flashes the other theme
            core.Navigate(Origin + "index.html");
        }

        static bool IsOurs(string uri) { return uri != null && uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase); }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!IsOurs(e.Source)) return; // ignore anything that is not our own page
            string raw;
            try { raw = e.TryGetWebMessageAsString(); } catch { return; }
            object reqId = null;
            try
            {
                var msg = _json.Deserialize<Dictionary<string, object>>(raw);
                reqId = msg.ContainsKey("id") ? msg["id"] : null;
                string cmd = msg.ContainsKey("cmd") ? msg["cmd"] as string : null;
                var args = msg.ContainsKey("args") && msg["args"] is Dictionary<string, object>
                    ? (Dictionary<string, object>)msg["args"] : new Dictionary<string, object>();

                object result;
                switch (cmd)
                {
                    case "ready":
                        _ready = true;
                        int ran = RunRulesSafe();
                        result = new Dictionary<string, object> {
                            { "autostart", Shortcuts.AutostartEnabled() },
                            { "lang", Strings.Lang },
                            { "theme", _theme },
                            { "autoChanged", ran } };
                        break;
                    case "setTheme":
                        object th;
                        string theme = NormalizeTheme(args.TryGetValue("theme", out th) ? th as string : null);
                        _store.SetSetting("theme", theme); // persist first: on failure nothing changes
                        _theme = theme;
                        ApplyTheme();
                        result = _theme; break;
                    case "hide": HideToTray(); result = true; break;
                    case "setAutostart":
                        Shortcuts.SetAutostart(args.ContainsKey("on") && args["on"] is bool && (bool)args["on"]);
                        _autoItem.Checked = Shortcuts.AutostartEnabled();
                        result = _autoItem.Checked; break;
                    case "setSetting":
                        result = _store.Handle(cmd, args);
                        object k, v;
                        if (args.TryGetValue("key", out k) && (k as string) == "lang")
                        {
                            Strings.Lang = args.TryGetValue("value", out v) && (v as string) == "pt" ? "pt" : "en";
                            LocalizeTray();
                        }
                        break;
                    default: result = _store.Handle(cmd ?? "", args); break;
                }
                Reply(reqId, true, result);
            }
            catch (Exception ex)
            {
                Program.Log(ex);
                Reply(reqId, false, ex is ArgumentException ? ex.Message : "Unexpected error.");
            }
        }

        void Reply(object id, bool ok, object data)
        {
            if (_web.CoreWebView2 == null) return;
            var payload = new Dictionary<string, object> { { "id", id }, { "ok", ok }, { ok ? "data" : "error", data } };
            _web.CoreWebView2.PostWebMessageAsString(_out.Serialize(payload));
        }

        void SendEvent(string name, object data = null)
        {
            if (_web.CoreWebView2 == null || !_ready) return;
            _web.CoreWebView2.PostWebMessageAsString(_out.Serialize(new Dictionary<string, object> { { "event", name }, { "data", data } }));
        }

        // ---------- Automations ----------

        int RunRulesSafe()
        {
            try { return _store.RunAll(); }
            catch (Exception ex) { Program.Log(ex); return 0; }
        }

        void OnRuleTimer(object sender, EventArgs e)
        {
            int n = RunRulesSafe();
            if (n > 0) SendEvent("refresh", n);
        }

        // ---------- Tray ----------

        void BuildTray()
        {
            var menu = new ContextMenuStrip();
            _openItem = new ToolStripMenuItem("", null, (s, e) => ShowFromTray());
            _newItem = new ToolStripMenuItem("", null, (s, e) => { ShowFromTray(); SendEvent("newNote"); });
            _autoItem = new ToolStripMenuItem("") { Checked = Shortcuts.AutostartEnabled() };
            _autoItem.Click += (s, e) =>
            {
                Shortcuts.SetAutostart(!_autoItem.Checked);
                _autoItem.Checked = Shortcuts.AutostartEnabled();
                SendEvent(_autoItem.Checked ? "autostartOn" : "autostartOff");
            };
            _exitItem = new ToolStripMenuItem("", null, (s, e) => ExitApp());
            menu.Items.AddRange(new ToolStripItem[] {
                _openItem, _newItem, new ToolStripSeparator(), _autoItem, new ToolStripSeparator(), _exitItem });
            LocalizeTray();

            _tray.Icon = AppIcon(16);
            _tray.Text = Program.AppName;
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleWindow(); };
        }

        void LocalizeTray()
        {
            _openItem.Text = Strings.T("open");
            _newItem.Text = Strings.T("newNote");
            _autoItem.Text = Strings.T("autostart");
            _exitItem.Text = Strings.T("exit");
        }

        void ToggleWindow()
        {
            if (Visible && WindowState != FormWindowState.Minimized && ContainsFocus) HideToTray();
            else ShowFromTray();
        }

        public void ShowFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            SetForegroundWindow(Handle);
            if (_web.CoreWebView2 != null) _web.Focus();
        }

        void HideToTray()
        {
            SaveBounds();
            Hide();
            if (!_hintShown && _db.Scalar("SELECT value FROM settings WHERE key='trayHint'") == null)
            {
                _hintShown = true;
                _db.Exec("INSERT OR REPLACE INTO settings(key,value) VALUES('trayHint','1')");
                _tray.ShowBalloonTip(4000, Program.AppName, Strings.T("trayHint"), ToolTipIcon.None);
            }
        }

        public void ExitApp()
        {
            _exiting = true;
            SaveBounds();
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // closing the window only hides it
                HideToTray();
                return;
            }
            _ruleTimer.Stop();
            UnregisterHotKey(Handle, HOTKEY_ID);
            SystemEvents.UserPreferenceChanged -= OnUserPrefChanged;
            _tray.Visible = false;
            _tray.Dispose();
            base.OnFormClosing(e);
            _db.Dispose();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID) // WM_HOTKEY
            {
                bool wasVisible = Visible && WindowState != FormWindowState.Minimized;
                ShowFromTray();
                if (wasVisible) SendEvent("newNote");
                return;
            }
            base.WndProc(ref m);
        }

        // ---------- Window position and theme ----------

        void LoadBounds()
        {
            Rectangle r = new Rectangle(0, 0, 1180, 760);
            bool max = false;
            try
            {
                var v = _db.Scalar("SELECT value FROM settings WHERE key='window'") as string;
                if (v != null)
                {
                    var p = v.Split(',');
                    r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                    max = p.Length > 4 && p[4] == "1";
                }
            }
            catch { }
            bool visibleOnScreen = false;
            foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r)) visibleOnScreen = true;
            if (!visibleOnScreen || r.X == 0 && r.Y == 0)
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                r.Width = Math.Min(r.Width, wa.Width - 40);
                r.Height = Math.Min(r.Height, wa.Height - 40);
                r.X = wa.X + (wa.Width - r.Width) / 2;
                r.Y = wa.Y + (wa.Height - r.Height) / 2;
            }
            Bounds = r;
            if (max) WindowState = FormWindowState.Maximized;
        }

        void SaveBounds()
        {
            try
            {
                if (WindowState == FormWindowState.Minimized || !Visible) return;
                var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                string v = r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (WindowState == FormWindowState.Maximized ? "1" : "0");
                _db.Exec("INSERT OR REPLACE INTO settings(key,value) VALUES('window',?)", v);
            }
            catch (Exception ex) { Program.Log(ex); }
        }

        void OnUserPrefChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            BeginInvoke(new Action(ApplyTheme));
        }

        static string NormalizeTheme(string t) { return t == "light" || t == "dark" ? t : "system"; }

        // Title bar, window background and web content all follow _theme.
        void ApplyTheme()
        {
            ApplyTitleBarTheme();
            ApplyBackColor();
            ApplyWebColorScheme();
        }

        void ApplyBackColor()
        {
            BackColor = IsDark() ? Color.FromArgb(28, 27, 25) : Color.FromArgb(246, 244, 239);
            _web.DefaultBackgroundColor = BackColor;
        }

        // The page's CSS uses prefers-color-scheme; WebView2 answers it from this setting
        // (Auto = the Windows app theme), which also themes scrollbars and native pickers.
        void ApplyWebColorScheme()
        {
            var core = _web.CoreWebView2;
            if (core == null) return;
            try
            {
                core.Profile.PreferredColorScheme =
                    _theme == "dark" ? CoreWebView2PreferredColorScheme.Dark :
                    _theme == "light" ? CoreWebView2PreferredColorScheme.Light :
                    CoreWebView2PreferredColorScheme.Auto;
            }
            catch (Exception ex) { Program.Log(ex); } // older WebView2 runtime: the page keeps following Windows
        }

        void ApplyTitleBarTheme()
        {
            try
            {
                int dark = IsDark() ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
            }
            catch { }
        }

        bool IsDark()
        {
            if (_theme == "dark") return true;
            if (_theme == "light") return false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static Icon AppIcon(int size = 32)
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "neverforget.ico");
            try { return File.Exists(p) ? new Icon(p, size, size) : SystemIcons.Application; }
            catch { return SystemIcons.Application; }
        }

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    }
}

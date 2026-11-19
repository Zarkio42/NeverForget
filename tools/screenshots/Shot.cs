// Renders web/index.html in demo mode inside WebView2 and saves the README screenshots
// to docs/screenshots. Run tools\screenshots\make.cmd from the repository root.
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class Shot
{
    // name, query string, color scheme
    static readonly string[][] Shots =
    {
        new[] { "board-light", "lang=en", "light" },
        new[] { "board-dark", "lang=en", "dark" },
        new[] { "wall", "lang=en&view=wall", "light" },
        new[] { "editor", "lang=en&edit=1", "light" },
        new[] { "automations", "lang=en&panel=auto", "dark" },
        new[] { "board-pt", "lang=pt", "light" },
    };

    [STAThread]
    static int Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string webDir = Path.Combine(root, "web");
        string outDir = Path.Combine(root, "docs", "screenshots");
        if (!File.Exists(Path.Combine(webDir, "index.html"))) { Console.Error.WriteLine("web/index.html not found under " + root); return 1; }
        Directory.CreateDirectory(outDir);
        string profile = Path.Combine(Path.GetTempPath(), "neverforget-shots-" + Guid.NewGuid().ToString("N"));
        int exit = 0;

        Application.EnableVisualStyles();
        var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(0, 0),
            ClientSize = new Size(1280, 760),
            ShowInTaskbar = false,
            TopMost = false,
        };
        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);

        form.Shown += async (s, e) =>
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, profile, null);
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("neverforget.example", webDir, CoreWebView2HostResourceAccessKind.Deny);
                await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__NF_FORCE_MOCK = true;");

                foreach (var sh in Shots)
                {
                    core.Profile.PreferredColorScheme = sh[2] == "dark"
                        ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
                    var done = new TaskCompletionSource<bool>();
                    EventHandler<CoreWebView2NavigationCompletedEventArgs> h = null;
                    h = (o, ev) => { core.NavigationCompleted -= h; done.TrySetResult(ev.IsSuccess); };
                    core.NavigationCompleted += h;
                    core.Navigate("https://neverforget.example/index.html?demo&" + sh[1]);
                    await done.Task;
                    await Task.Delay(1500); // let entrance animations finish
                    string file = Path.Combine(outDir, sh[0] + ".png");
                    using (var fs = File.Create(file))
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
                    Console.WriteLine("saved " + file);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
            form.Close();
        };

        Application.Run(form);
        web.Dispose();
        try { Directory.Delete(profile, true); } catch { }
        return exit;
    }
}

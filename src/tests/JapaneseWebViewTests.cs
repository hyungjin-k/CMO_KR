// Real WebView2 fixture for the Japanese prose scope. Needs the Edge WebView2 runtime and the
// SDK binaries copied out of the game directory by scripts/test_ja.ps1; never starts CMO.
//
//   JapaneseWebViewTests.exe <CMOLocalizerJa.dll> <CMOLocalizerJa.json> <work dir> [<scenario html dir>]
//
// Compile -platform:x64 -codepage:65001 against Microsoft.Web.WebView2.Core/WinForms.dll.
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Same type name as the game's window, so the engine treats it as a prose window.
internal sealed class NewMessageForm : Form { }
// Any other window: its WebView2 must stay untouched.
internal sealed class OtherWindow : Form { }

internal static class Program
{
    private static int failures, passes;
    private static Type engine;

    private static void Check(bool ok, string message)
    {
        if (ok) { passes++; Console.WriteLine("PASS " + message); }
        else { failures++; Console.WriteLine("FAIL " + message); }
    }

    private static object Call(string name, params object[] args)
    {
        MethodInfo method = engine.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }

    private const string Page1 = "<html><body>"
        + "<p id='p1'>Good morning!</p>"
        + "<p id='p2'>Today you will impersonate the commander of the Ramat David Air Base in Northern Israel and you will learn how to use your air assets.</p>"
        + "<p id='b1'>You have 6 <b>F-16I Sufa</b> and 2 Gulfstream G550 AEW Nahshon under your command.</p>"
        + "<ul><li id='l1'>Preparing an aircraft for launch.</li><li id='l2'>Launching an aircraft.</li></ul>"
        + "<div id='br'>Flying manually the plane.<br>Return to base and land.</div>"
        + "<p id='link'><a href='#x'>Welcome to the Basic Manual Flight tutorial.</a></p>"
        + "<p id='unk'>Unknown fixture paragraph stays English.</p>"
        + "<textarea id='ta'>Good morning!</textarea>"
        + "<div id='ed' contenteditable='true'>Launching an aircraft.</div>"
        + "<p id='ws'>  Good \n\n   morning!  </p>"
        + "</body></html>";

    private const string Page2 = "<html><body><p id='q1'>Good morning!</p><p id='q2'>Click on \"OK-Ready\".</p></body></html>";

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (IOException) { }
        if (args.Length != 3 && args.Length != 4)
        {
            Console.WriteLine("usage: <CMOLocalizerJa.dll> <CMOLocalizerJa.json> <work dir> [<scenario html dir>]");
            return 2;
        }
        try
        {
            engine = Assembly.LoadFrom(args[0]).GetType("CMOLocalizer.Japanese.JapaneseUiLocalization", true);
            Directory.CreateDirectory(args[2]);
            Call("SetDiagnosticsDirectory", args[2]);
            object[] load = { args[1], null };
            Check((bool)Call("LoadDictionaryFile", load), "dictionary loaded (" + (load[1] ?? "ok") + ")");
            Application.EnableVisualStyles();
            NewMessageForm form = Window(new NewMessageForm(), 0);
            OtherWindow other = Window(new OtherWindow(), 600);
            WebView2 view = AddView(form);
            WebView2 otherView = AddView(other);
            form.Shown += async delegate
            {
                try
                {
                    other.Show();
                    await Run(form, view, other, otherView, args[2], args.Length == 4 ? args[3] : null);
                }
                catch (Exception ex) { Check(false, "unexpected exception: " + ex); }
                finally
                {
                    other.Close();
                    form.Close();
                }
            };
            Application.Run(form);
        }
        catch (Exception ex) { Check(false, "unexpected exception: " + ex); }
        Console.WriteLine(failures == 0 ? "ALL PASS (" + passes + ")" : "FAILED " + failures + " of " + (passes + failures));
        return failures == 0 ? 0 : 1;
    }

    private static T Window<T>(T form, int offset) where T : Form
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-3000 - offset, -3000);
        form.Size = new Size(500, 400);
        form.ShowInTaskbar = false;
        return form;
    }

    private static WebView2 AddView(Form form)
    {
        WebView2 view = new WebView2();
        view.Dock = DockStyle.Fill;
        form.Controls.Add(view);
        return view;
    }

    private static async Task Run(NewMessageForm form, WebView2 view, OtherWindow other, WebView2 otherView, string work,
                                  string pagesDir)
    {
        CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(work, "wv2-user-data"));
        await view.EnsureCoreWebView2Async(env);
        await otherView.EnsureCoreWebView2Async(env);
        // What the watcher does when it finds the windows.
        Call("TranslateForm", form);
        Call("TranslateForm", other);

        await Navigate(view, Page1);
        await Navigate(otherView, Page1);
        bool translated = await WaitFor(view, "document.getElementById('p1').innerText", "おはようございます！");
        Check(translated, "prose window: paragraph translated after the page loaded");
        Check((await Text(view, "p2")).Contains("ラマト・ダビド空軍基地"), "long paragraph translated");
        Check((await Text(view, "b1")).StartsWith("指揮下には F-16I Sufa 6機"), "paragraph with <b> formatting translated");
        Check(await Text(view, "l1") == "航空機の発進準備。" && await Text(view, "l2") == "航空機の発進。", "list items translated");
        string br = await Text(view, "br");
        Check(br.Contains("航空機の手動操縦。") && br.Contains("基地への帰還と着陸。"), "<br>-separated lines translated separately");
        Check(await Text(view, "ws") == "おはようございます！", "whitespace and line breaks are normalized before lookup");
        Check(await Eval(view, "document.querySelectorAll('#link a').length + '|' + document.querySelector('#link a').textContent")
              == "1|Welcome to the Basic Manual Flight tutorial.", "paragraph containing a link is left intact");
        Check(await Text(view, "unk") == "Unknown fixture paragraph stays English.", "unknown paragraph stays English");
        Check(await Eval(view, "document.getElementById('ta').value") == "Good morning!", "textarea content never written");
        Check(await Text(view, "ed") == "Launching an aircraft.", "contenteditable region never written");

        // A new document in the same control: the engine's own hooks translate it.
        await Navigate(view, Page2);
        Check(await WaitFor(view, "document.getElementById('q2').innerText", "\"OK-Ready\" をクリックします。"),
              "re-navigation is translated without another scan");
        Check(await Text(view, "q1") == "おはようございます！", "second document fully translated");

        await Task.Delay(1500);
        Check(await Eval(otherView, "document.getElementById('p1').innerText") == "Good morning!",
              "WebView2 outside the prose windows is not translated");

        string tsv = Path.Combine(work, "CMOLocalizerJa_untranslated.tsv");
        string misses = File.Exists(tsv) ? File.ReadAllText(tsv, Encoding.UTF8) : "";
        Check(misses.Contains("prose\tNewMessageForm\tUnknown fixture paragraph stays English."),
              "untranslated paragraph recorded with its window");
        Check(!misses.Contains("Good morning!"), "translated paragraphs are not recorded");
        if (pagesDir != null)
        {
            // The real scenario pages (read-only): the in-page paragraph split must agree with
            // scripts/extract_ja_prose.py, so a fully translated set records no new prose misses.
            string before = File.Exists(tsv) ? File.ReadAllText(tsv, Encoding.UTF8) : "";
            string[] pages = Directory.GetFiles(pagesDir, "*.html");
            Array.Sort(pages, StringComparer.Ordinal);
            int japanese = 0;
            foreach (string page in pages)
            {
                await NavigateUri(view, new Uri(page).AbsoluteUri);
                if (await WaitForJapanese(view)) japanese++;
            }
            string after = File.Exists(tsv) ? File.ReadAllText(tsv, Encoding.UTF8) : "";
            string added = after.Length > before.Length ? after.Substring(before.Length) : "";
            int newMisses = 0;
            foreach (string line in added.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("prose\t", StringComparison.Ordinal)) { newMisses++; if (newMisses <= 5) Console.WriteLine("  miss: " + line); }
            }
            Check(pages.Length > 0 && japanese == pages.Length && newMisses == 0,
                  "real scenario pages: " + japanese + "/" + pages.Length + " shown in Japanese, " + newMisses + " untranslated paragraphs");
        }
        string stats = (string)Call("Stats");
        Check(stats.Contains("proseWrites=") && !stats.Contains("proseWrites=0;") && !stats.EndsWith("proseWrites=0"),
              "engine counts prose writes (" + stats + ")");
    }

    private static Task NavigateUri(WebView2 view, string uri)
    {
        TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
        EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
        handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            view.NavigationCompleted -= handler;
            done.TrySetResult(e.IsSuccess);
        };
        view.NavigationCompleted += handler;
        view.CoreWebView2.Navigate(uri);
        return done.Task;
    }

    private static async Task<bool> WaitForJapanese(WebView2 view)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            string text = await Eval(view, "document.body ? document.body.innerText : ''");
            foreach (char c in text)
            {
                if ((c >= (char)0x3040 && c <= (char)0x30FF) || (c >= (char)0x4E00 && c <= (char)0x9FFF)) return true;
            }
            await Task.Delay(150);
        }
        return false;
    }

    private static Task Navigate(WebView2 view, string html)
    {
        TaskCompletionSource<bool> done = new TaskCompletionSource<bool>();
        EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
        handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            view.NavigationCompleted -= handler;
            done.TrySetResult(e.IsSuccess);
        };
        view.NavigationCompleted += handler;
        view.NavigateToString(html);
        return done.Task;
    }

    private static async Task<string> Eval(WebView2 view, string expression)
    {
        // encodeURIComponent keeps the JSON result ASCII without backslashes or quotes.
        string raw = await view.ExecuteScriptAsync("encodeURIComponent(String(" + expression + "))");
        if (raw == null || raw.Length < 2 || raw[0] != '"') return raw;
        return Uri.UnescapeDataString(raw.Substring(1, raw.Length - 2));
    }

    private static Task<string> Text(WebView2 view, string id)
    {
        return Eval(view, "document.getElementById('" + id + "').innerText");
    }

    private static async Task<bool> WaitFor(WebView2 view, string expression, string expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await Eval(view, expression) == expected) return true;
            await Task.Delay(150);
        }
        Console.WriteLine("  last value: " + await Eval(view, expression));
        return false;
    }
}

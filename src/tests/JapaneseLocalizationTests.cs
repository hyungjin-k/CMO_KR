// Japanese minimal UI fixtures. No game assembly, no CMO process, no installed files.
//
//   fixture.exe unit      <CMOLocalizerJa.dll> <CMOLocalizerJa.json> <scratch-dir>
//   fixture.exe isolation <CMOLocalizerJa.dll> [<Chinese CMOLocalizer.dll for the negative control>]
//   fixture.exe host      (started as Command.exe of a synthetic game directory whose
//                          Command.exe.config was patched by scripts/manage_ja_patch.py)
//
// Compile with -codepage:65001 (this file contains Japanese expectations).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

internal sealed class OptionsFixture : Form
{
    internal ToolTip Tips = new ToolTip();
}

internal sealed class DarkSectionPanel : Panel
{
    public string SectionHeader { get; set; }
}

// Like DarkUI's owner-drawn controls (DarkButton hides Text with `new` and repaints
// only in its own setter): writing Control.Text alone does not repaint it.
internal sealed class PaintedText : Control
{
    internal int Repaints;
    protected override void OnInvalidated(InvalidateEventArgs e) { Repaints++; base.OnInvalidated(e); }
}

internal static class Program
{
    private static int failures;
    private static int passes;

    private static void Check(bool ok, string message)
    {
        if (ok) { passes++; Console.WriteLine("PASS " + message); }
        else { failures++; Console.WriteLine("FAIL " + message); }
    }

    private sealed class Engine
    {
        private readonly Type type;
        internal Engine(Assembly assembly)
        {
            type = assembly.GetType("CMOLocalizer.Japanese.JapaneseUiLocalization", true);
        }
        private object Call(string name, params object[] args)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            try { return method.Invoke(null, args); }
            catch (TargetInvocationException ex) { throw ex.InnerException; }
        }
        internal bool Load(string json, out string error)
        {
            object[] args = { json, null };
            bool ok = (bool)Call("LoadDictionaryText", args);
            error = (string)args[1];
            return ok;
        }
        internal bool LoadFile(string path, out string error)
        {
            object[] args = { path, null };
            bool ok = (bool)Call("LoadDictionaryFile", args);
            error = (string)args[1];
            return ok;
        }
        internal string Menu(string text) { return (string)Call("TranslateMenuText", text); }
        internal string Options(string text) { return (string)Call("TranslateOptionsText", text); }
        internal void Form(Form form) { Call("TranslateForm", form); }
        internal bool IsOptions(Form form) { return (bool)Call("IsOptionsForm", form); }
        internal void Diagnostics(string dir) { Call("SetDiagnosticsDirectory", dir); }
        internal void Reset() { Call("ResetCounters"); }
        internal int Stat(string name)
        {
            foreach (string part in ((string)Call("Stats")).Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv[0] == name) return int.Parse(kv[1], CultureInfo.InvariantCulture);
            }
            throw new KeyNotFoundException(name);
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); }
        catch (IOException) { }
        try
        {
            string mode = args.Length > 0 ? args[0] : "";
            if (mode == "unit" && args.Length == 4) Unit(args[1], args[2], args[3]);
            else if (mode == "isolation" && args.Length >= 2) Isolation(args[1], args.Length > 2 ? args[2] : null);
            else if (mode == "host") Host();
            else
            {
                Console.WriteLine("usage: unit <dll> <json> <scratch> | isolation <dll> [<zh dll>] | host");
                return 2;
            }
        }
        catch (Exception ex)
        {
            Check(false, "unexpected exception: " + ex);
        }
        Console.WriteLine(failures == 0 ? "ALL PASS (" + passes + ")" : "FAILED " + failures + " of " + (passes + failures));
        return failures == 0 ? 0 : 1;
    }

    // ── dictionaries ────────────────────────────────────────────────────

    private static string Esc(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string Doc(string language, string offsets, string[] exact, string[] menu, string[] options)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"_meta\":{\"language\":").Append(Esc(language)).Append(",\"profile\":\"ja-fixture\",\"scopes\":{\"menu\":[");
        for (int i = 0; i < menu.Length; i++) sb.Append(i > 0 ? "," : "").Append(Esc(menu[i]));
        sb.Append("],\"options\":[");
        for (int i = 0; i < options.Length; i++) sb.Append(i > 0 ? "," : "").Append(Esc(options[i]));
        sb.Append("]}},\"comments\":{},\"offsets\":").Append(offsets).Append(",\"exact\":{");
        for (int i = 0; i < exact.Length; i += 2) sb.Append(i > 0 ? "," : "").Append(Esc(exact[i])).Append(":").Append(Esc(exact[i + 1]));
        sb.Append("},\"partial\":{},\"words\":{}}");
        return sb.ToString();
    }

    private static void Unit(string dllPath, string dictPath, string scratch)
    {
        Engine engine = new Engine(Assembly.LoadFrom(dllPath));
        Directory.CreateDirectory(scratch);
        string tsv = Path.Combine(scratch, "CMOLocalizerJa_untranslated.tsv");
        if (File.Exists(tsv)) File.Delete(tsv);
        engine.Diagnostics(scratch);

        // Negative control first: with no dictionary the probes must observe English.
        Form probe = MainFixture();
        engine.Form(probe);
        Check(engine.Menu("File") == "File" && MenuItem(probe, 0).Text == "File",
              "negative control: without a dictionary nothing is translated (the probe can fail)");

        string[] good = { "File", "ファイル", "Apply", "適用" };
        string[] menuKeys = { "File" };
        string[] optionKeys = { "Apply" };
        string error;
        Check(!engine.Load("{\"_meta\":", out error), "reject malformed JSON (" + error + ")");
        Check(!engine.Load(Doc("zh", "{}", good, menuKeys, optionKeys), out error), "reject non-Japanese language (" + error + ")");
        Check(!engine.Load(Doc("ja", "{\"1789870\":\"x\"}", good, menuKeys, optionKeys), out error),
              "reject pool injections in the UI profile (" + error + ")");
        Check(!engine.Load(Doc("ja", "{}", good, new[] { "Missing" }, optionKeys), out error), "reject scope key absent from exact (" + error + ")");
        Check(!engine.Load(Doc("ja", "{}", new[] { "File", "ファイル", "File", "x" }, menuKeys, optionKeys), out error),
              "reject duplicate JSON keys (" + error + ")");
        Check(!engine.Load(Doc("ja", "{}", new[] { "File", "ファイル", "ファイル", "File" }, new[] { "File", "ファイル" }, optionKeys), out error),
              "reject a value that is also a source key (" + error + ")");
        Check(!engine.Load(Doc("ja", "{}", good, menuKeys, optionKeys).Replace("\"words\":{}", "\"words\":{},\"extra\":{}"), out error),
              "reject unknown top-level sections (" + error + ")");
        Check(engine.Menu("File") == "File", "rejected dictionaries leave the UI in English");
        // JSON escapes are spliced in after Doc() so Esc() cannot double the backslashes.
        string bs = ((char)92).ToString();   // one backslash
        string escaped = Doc("ja", "{}", new[] { "File", "@KANA@", "Apply", "適用" }, menuKeys, optionKeys)
            .Replace("@KANA@", bs + "u30d5" + bs + "u30a1" + bs + "u30a4" + bs + "u30eb");
        Check(escaped.Contains(bs + "u30d5") && engine.Load(escaped, out error) && engine.Menu("File") == "ファイル",
              "JSON unicode escapes decode to kana");

        Check(engine.LoadFile(dictPath, out error), "load the built dictionary (" + (error ?? "ok") + ")");

        // ── lookups ──
        Check(engine.Menu("File") == "ファイル", "menu: File -> kana");
        Check(engine.Options("Apply") == "適用", "options: Apply -> kanji");
        Check(engine.Menu("Use Autosave") == "Use Autosave", "scope: options text is not a menu translation");
        Check(engine.Options("File") == "File", "scope: menu text is not an options translation");
        Check(engine.Menu("__JA_UNKNOWN_7F21__") == "__JA_UNKNOWN_7F21__", "unknown text stays English");
        Check(engine.Menu(engine.Menu("File")) == engine.Menu("File"), "idempotent: T(T(x)) == T(x)");
        Check(engine.Options(" Use Autosave ") == " 自動保存を使用 ", "trimmed match keeps the original padding");
        Check(engine.Options("Every 30 seconds") == "30秒ごと", "options rule: Every N seconds");
        Check(engine.Options("Every 1 minute") == "1分ごと", "options rule: Every N minute(s)");
        Check(engine.Options("Every x seconds") == "Every x seconds", "options rule refuses non-numeric values");
        Check(engine.Menu("Every 30 seconds") == "Every 30 seconds", "options rules never apply to menus");
        Check(engine.Options("Vertical scaling: x3") == "垂直方向の倍率: x3", "options rule: Vertical scaling: xN");

        // ── main window: menus only ──
        engine.Reset();
        Form main = MainFixture();
        ToolStripMenuItem file = MenuItem(main, 0);
        ToolStripMenuItem save = (ToolStripMenuItem)file.DropDownItems[0];
        ToolStripMenuItem recent = (ToolStripMenuItem)file.DropDownItems[2];
        ToolStripMenuItem view = MenuItem(main, 1);
        ToolStripMenuItem panUp = (ToolStripMenuItem)view.DropDownItems[0];
        Label autosaveLabel = (Label)main.Controls["autosave"];
        Button cancel = (Button)main.Controls["cancel"];
        engine.Form(main);
        Check(file.Text == "ファイル" && save.Text == "保存" && view.Text == "表示", "main menu and submenu translated");
        Check(panUp.ShortcutKeyDisplayString == "上矢印 / テンキー8", "shortcut display text translated (display only)");
        Check(panUp.ShortcutKeys == (Keys.Control | Keys.Up), "shortcut key binding unchanged");
        Check(recent.Text == "C:\\Users\\me\\Scenarios\\x.scen", "recent-file entry untouched");
        Check(autosaveLabel.Text == "Use Autosave" && cancel.Text == "Cancel", "non-menu controls outside Game Options untouched");
        int writes = engine.Stat("menuWrites");
        engine.Form(main);
        Check(engine.Stat("menuWrites") == writes, "second scan writes nothing (" + writes + " writes total)");

        // The feasibility audit's reproduction: kana values were lost after the game re-set Text.
        file.Text = "File";
        Check(file.Text == "ファイル", "kana re-applied after the game rewrites menu text");
        save.Text = "Save";
        Check(save.Text == "保存", "kanji re-applied after the game rewrites menu text");
        file.Text = "Totally New Caption";
        Check(file.Text == "Totally New Caption", "game-written text without a translation is left alone");
        file.Text = "File";

        ToolStripMenuItem saveAs = new ToolStripMenuItem("Save As...");
        file.DropDownItems.Add(saveAs);
        Raise(typeof(ToolStripDropDownItem), file, "OnDropDownShow", EventArgs.Empty);   // raises DropDownOpening
        Check(saveAs.Text == "名前を付けて保存...", "items added before a drop-down opens are translated on opening");

        ContextMenuStrip context = main.Controls["map"].ContextMenuStrip;
        Raise(typeof(ToolStripDropDown), context, "OnOpening", new CancelEventArgs());
        Check(context.Items[0].Text == "ズームイン", "context menu translated when it opens");

        // ── Game Options window ──
        engine.Reset();
        OptionsFixture options = OptionsFixtureForm();
        CheckBox autosave = (CheckBox)Find(options, "autosave");
        TextBox input = (TextBox)Find(options, "input");
        ComboBox speed = (ComboBox)Find(options, "speed");
        NumericUpDown number = (NumericUpDown)Find(options, "number");
        DataGridView grid = (DataGridView)Find(options, "grid");
        DarkSectionPanel section = (DarkSectionPanel)Find(options, "section");
        PaintedText painted = (PaintedText)Find(options, "painted");
        IntPtr paintedHandle = painted.Handle;   // live windows have handles; Invalidate is a no-op without one
        painted.Repaints = 0;
        Label unknown = (Label)Find(options, "unknown");
        Label fight = (Label)Find(options, "fight");
        Button ok = (Button)Find(options, "ok");
        Button cancelOptions = (Button)Find(options, "cancelOptions");
        TabControl tabs = (TabControl)Find(options, "tabs");
        engine.Form(options);
        Check(engine.IsOptions(options), "Game Options window recognised");
        Check(options.Text == "ゲーム設定" && engine.IsOptions(options), "title translated and window still recognised afterwards");
        Check(tabs.TabPages[0].Text == "一般" && tabs.TabPages[1].Text == "単位系", "tab pages translated");
        Check(autosave.Text == "自動保存を使用" && autosave.Checked, "checkbox text translated, check state preserved");
        Check(Find(options, "every15").Text == "15秒ごと" && Find(options, "every30").Text == "30秒ごと", "exact and rule-based interval labels");
        Check(input.Text == "Use Autosave", "text input never written");
        Check(speed.Items.Count == 2 && (string)speed.Items[0] == "Knots" && speed.SelectedIndex == 0 && speed.Text == "Knots",
              "combo items, selection and Text preserved");
        Check(number.Value == 42, "numeric input preserved");
        Check(grid.Columns[0].HeaderText == "メッセージの種類" && grid.Columns[0].Name == "colType", "grid header translated, column name kept");
        Check(section.SectionHeader == "パフォーマンス", "custom SectionHeader translated");
        Check(painted.Text == "音楽の音量" && painted.Repaints >= 1,
              "owner-drawn control is asked to repaint after translation (repaints=" + painted.Repaints + ")");
        Check(options.Tips.GetToolTip(autosave) == "追加のメモリ保護", "tooltip translated");
        Check(ok.Text == "OK" && cancelOptions.Text == "キャンセル", "OK stays OK, Cancel translated");
        Check(unknown.Text == "Unknown fixture label", "unknown options label stays English");
        autosave.Text = "Use Autosave";
        Check(autosave.Text == "自動保存を使用", "options text re-applied after the game rewrites it");

        // A hostile game handler that always writes English back: bounded, no recursion.
        engine.Reset();
        for (int i = 0; i < 200; i++)
        {
            fight.Text = "x";
            fight.Text = "Use game sounds";
        }
        int rewrites = engine.Stat("eventRewrites");
        Check(rewrites <= 31 && engine.Stat("blockedRewrites") >= 1,
              "write-back storm is throttled (rewrites=" + rewrites + ", blocked=" + engine.Stat("blockedRewrites") + ")");
        Check(autosaveLabel.Text == "Use Autosave", "options vocabulary still not applied outside Game Options");

        string misses = File.Exists(tsv) ? File.ReadAllText(tsv, Encoding.UTF8) : "";
        Check(misses.Contains("options\tLabel\tUnknown fixture label"), "untranslated options label recorded for the next round");
        Check(misses.Contains("menu\tToolStripMenuItem\tFixture Only Command"), "untranslated menu item recorded");
        Check(!misses.Contains("x.scen") && !misses.Contains("Users"), "recent-file paths are never recorded");
        Check(!misses.Contains("ファイル"), "translated text is not recorded as missing");
    }

    private static void Raise(Type declaring, object target, string method, EventArgs args)
    {
        MethodInfo m = declaring.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        m.Invoke(target, new object[] { args });
    }

    private static ToolStripMenuItem MenuItem(Form form, int index)
    {
        foreach (Control c in form.Controls)
        {
            MenuStrip strip = c as MenuStrip;
            if (strip != null) return (ToolStripMenuItem)strip.Items[index];
        }
        throw new InvalidOperationException("no menu");
    }

    private static Control Find(Control root, string name)
    {
        Control[] found = root.Controls.Find(name, true);
        if (found.Length != 1) throw new InvalidOperationException("fixture control " + name);
        return found[0];
    }

    private static Form MainFixture()
    {
        Form form = new Form();
        form.Text = "Fixture Main";
        MenuStrip strip = new MenuStrip();
        ToolStripMenuItem file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(new ToolStripMenuItem("Save"));
        file.DropDownItems.Add(new ToolStripMenuItem("Fixture Only Command"));
        file.DropDownItems.Add(new ToolStripMenuItem("C:\\Users\\me\\Scenarios\\x.scen"));
        ToolStripMenuItem view = new ToolStripMenuItem("View");
        ToolStripMenuItem panUp = new ToolStripMenuItem("Pan Map Up");
        panUp.ShortcutKeys = Keys.Control | Keys.Up;
        panUp.ShortcutKeyDisplayString = "Up Arrow / Num 8";
        view.DropDownItems.Add(panUp);
        strip.Items.Add(file);
        strip.Items.Add(view);
        form.Controls.Add(strip);
        form.MainMenuStrip = strip;
        Label label = new Label();
        label.Name = "autosave";
        label.Text = "Use Autosave";
        form.Controls.Add(label);
        Button cancel = new Button();
        cancel.Name = "cancel";
        cancel.Text = "Cancel";
        form.Controls.Add(cancel);
        Panel map = new Panel();
        map.Name = "map";
        ContextMenuStrip context = new ContextMenuStrip();
        context.Items.Add(new ToolStripMenuItem("Zoom In"));
        map.ContextMenuStrip = context;
        form.Controls.Add(map);
        return form;
    }

    private static OptionsFixture OptionsFixtureForm()
    {
        OptionsFixture form = new OptionsFixture();
        form.Text = "Game Options";
        TabControl tabs = new TabControl();
        tabs.Name = "tabs";
        TabPage general = new TabPage("General");
        TabPage units = new TabPage("Unit System");
        tabs.TabPages.Add(general);
        tabs.TabPages.Add(units);
        form.Controls.Add(tabs);

        CheckBox autosave = new CheckBox();
        autosave.Name = "autosave";
        autosave.Text = "Use Autosave";
        autosave.Checked = true;
        form.Tips.SetToolTip(autosave, "Extra memory protection");
        general.Controls.Add(autosave);
        AddLabel(general, "every15", "Every 15 seconds");
        AddLabel(general, "every30", "Every 30 seconds");
        AddLabel(general, "unknown", "Unknown fixture label");
        Label fight = AddLabel(general, "fight", "Use game sounds");
        fight.TextChanged += delegate { if (fight.Text != "Use game sounds" && fight.Text != "x") fight.Text = "Use game sounds"; };

        TextBox input = new TextBox();
        input.Name = "input";
        input.Text = "Use Autosave";
        general.Controls.Add(input);
        ComboBox speed = new ComboBox();
        speed.Name = "speed";
        speed.DropDownStyle = ComboBoxStyle.DropDownList;
        speed.Items.Add("Knots");
        speed.Items.Add("Kph");
        units.Controls.Add(speed);
        speed.SelectedIndex = 0;
        NumericUpDown number = new NumericUpDown();
        number.Name = "number";
        number.Value = 42;
        units.Controls.Add(number);
        DataGridView grid = new DataGridView();
        grid.Name = "grid";
        grid.Columns.Add("colType", "Message Type");
        units.Controls.Add(grid);
        DarkSectionPanel section = new DarkSectionPanel();
        section.Name = "section";
        section.SectionHeader = "Performance";
        units.Controls.Add(section);
        PaintedText painted = new PaintedText();
        painted.Name = "painted";
        painted.Text = "Music Volume";
        units.Controls.Add(painted);

        Button ok = new Button();
        ok.Name = "ok";
        ok.Text = "OK";
        form.Controls.Add(ok);
        Button cancel = new Button();
        cancel.Name = "cancelOptions";
        cancel.Text = "Cancel";
        form.Controls.Add(cancel);
        return form;
    }

    private static Label AddLabel(Control parent, string name, string text)
    {
        Label label = new Label();
        label.Name = name;
        label.Text = text;
        parent.Controls.Add(label);
        return label;
    }

    // ── isolation: the Japanese DLL contains no Chinese engine ───────────

    private static string[] IsolationFindings(string path)
    {
        List<string> findings = new List<string>();
        Assembly assembly = Assembly.LoadFile(Path.GetFullPath(path));
        foreach (Type type in SafeTypes(assembly))
        {
            if (type.Name == "CustomAppDomainManager" || type.Name == "LocalizerDomainManager")
                findings.Add("type " + type.FullName);
        }
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
        {
            if (reference.Name.StartsWith("Presentation", StringComparison.Ordinal) || reference.Name == "WindowsBase")
                findings.Add("WPF reference " + reference.Name);
        }
        byte[] bytes = File.ReadAllBytes(path);
        foreach (string marker in new[] { "Microsoft YaHei", "zh-CN", "CMOLocalizer_PoolDump", "SetupChineseDateTimeCulture" })
        {
            if (IndexOf(bytes, Encoding.Unicode.GetBytes(marker)) >= 0 || IndexOf(bytes, Encoding.UTF8.GetBytes(marker)) >= 0)
                findings.Add("marker " + marker);
        }
        return findings.ToArray();
    }

    private static Type[] SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return Array.FindAll(ex.Types, delegate(Type t) { return t != null; }); }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    private static void Isolation(string japaneseDll, string chineseDll)
    {
        Assembly assembly = Assembly.LoadFile(Path.GetFullPath(japaneseDll));
        AssemblyName name = assembly.GetName();
        Check(name.Name == "CMOLocalizerJa" && name.Version == new Version(1, 0, 0, 0),
              "assembly identity " + name.Name + " " + name.Version);
        List<string> exported = new List<string>();
        foreach (Type type in assembly.GetExportedTypes()) exported.Add(type.FullName);
        exported.Sort(StringComparer.Ordinal);
        Check(string.Join(",", exported.ToArray()) ==
              "CMOLocalizer.Japanese.JapaneseDomainManager,CMOLocalizer.Japanese.JapaneseUiLocalization",
              "public surface is only the domain manager and the UI translator (" + string.Join(",", exported.ToArray()) + ")");
        Type manager = assembly.GetType("CMOLocalizer.Japanese.JapaneseDomainManager", true);
        Check(typeof(AppDomainManager).IsAssignableFrom(manager) && manager.GetConstructor(Type.EmptyTypes) != null,
              "domain manager derives from AppDomainManager with a public constructor");
        string[] findings = IsolationFindings(japaneseDll);
        Check(findings.Length == 0, "no Chinese engine types, WPF references or Chinese runtime markers ("
              + string.Join("; ", findings) + ")");
        if (chineseDll != null)
        {
            string[] control = IsolationFindings(chineseDll);
            Check(control.Length >= 3, "negative control: the same probe flags the Chinese engine ("
                  + string.Join("; ", control) + ")");
        }
    }

    // ── host: the real CLR loading path ─────────────────────────────────

    private static void Host()
    {
        AppDomainManager manager = AppDomain.CurrentDomain.DomainManager;
        Check(manager != null && manager.GetType().FullName == "CMOLocalizer.Japanese.JapaneseDomainManager",
              "CLR loaded the Japanese domain manager from Command.exe.config ("
              + (manager == null ? "none" : manager.GetType().AssemblyQualifiedName) + ")");
        Check(CultureInfo.GetCultureInfo("en-US").DateTimeFormat.DayNames[0] == "Sunday"
              && CultureInfo.InvariantCulture.DateTimeFormat.DayNames[0] == "Sunday",
              "no culture patching (en-US and invariant day names stay English)");

        Application.EnableVisualStyles();
        Form main = MainFixture();
        OptionsFixture options = OptionsFixtureForm();
        foreach (Form form in new Form[] { main, options })
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-3000, -3000);
            form.ShowInTaskbar = false;
        }
        ToolStripMenuItem file = MenuItem(main, 0);
        CheckBox autosave = (CheckBox)Find(options, "autosave");
        Label outside = (Label)main.Controls["autosave"];
        bool translated = false;
        bool refreshed = false;
        string menuSeen = "", optionSeen = "", titleSeen = "", outsideSeen = "";
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        Timer timer = new Timer();
        timer.Interval = 100;
        timer.Tick += delegate
        {
            if (!options.Visible) options.Show();
            if (!translated && file.Text == "ファイル" && autosave.Text == "自動保存を使用" && options.Text == "ゲーム設定")
            {
                translated = true;
                file.Text = "File";
                refreshed = file.Text == "ファイル";
            }
            if (translated || DateTime.UtcNow > deadline)
            {
                // Read before Close(): disposed controls no longer report their text.
                menuSeen = file.Text;
                optionSeen = autosave.Text;
                titleSeen = options.Text;
                outsideSeen = outside.Text;
                timer.Stop();
                options.Close();
                main.Close();
            }
        };
        timer.Start();
        Application.Run(main);
        Check(translated, "watcher translated the menu and Game Options of live windows (menu=" + menuSeen
              + ", option=" + optionSeen + ", title=" + titleSeen + ")");
        Check(refreshed, "game-style rewrite of a menu caption is re-translated immediately");
        Check(outsideSeen == "Use Autosave", "options vocabulary not applied to the main window (" + outsideSeen + ")");
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string log = Path.Combine(baseDir, "CMOLocalizerJa.log");
        string logText = File.Exists(log) ? File.ReadAllText(log, Encoding.UTF8) : "";
        Check(logText.Contains("dictionary loaded") && logText.Contains("Game Options window detected"),
              "engine log records dictionary load and window detection");
        Check(!File.Exists(Path.Combine(baseDir, "CMOLocalizer.log")) && !File.Exists(Path.Combine(baseDir, "CMOLocalizer_Crash.log")),
              "no Chinese engine log files were created");
    }
}

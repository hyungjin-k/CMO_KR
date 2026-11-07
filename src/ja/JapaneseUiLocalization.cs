// Japanese minimal UI engine (profile ja-minimal-ui-v1). C# 5, .NET Framework 4.x.
//
// This file is compiled ALONE into CMOLocalizerJa.dll by scripts/build_ja.ps1. It
// shares no code or state with the Chinese engine in src/engine: there is no
// string-pool injection, map tile server, culture patching, startup-dialog
// dismissal, font replacement, WPF/WebView/database/HTML handling.
//
// Contract
//   * Only exact display text listed in the dictionary's _meta.scopes is replaced.
//       menu    : ToolStripItem Text / ShortcutKeyDisplayString / ToolTipText of menu
//                 bars, context menus and toolbar drop-downs, in any window.
//       options : Text, SectionHeader, ToolTip and DataGridView column headers inside
//                 the Game Options window only.
//   * Inputs and selectors are never written: TextBoxBase, ComboBox, ListControl,
//     UpDownBase, DateTimePicker, LinkLabel and custom *TextBox/*EditBox/*Edit controls
//     keep their text, items and selection.
//   * A translated value is never a source key of its scope (checked at load), so
//     re-applying is idempotent. "Already translated" means "not a source key", not
//     "contains Chinese", which is why kana values survive game refreshes.
//   * Unknown text stays English and is recorded, deduplicated and capped, in
//     CMOLocalizerJa_untranslated.tsv for the next dictionary round.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CMOLocalizer.Japanese
{
    /// <summary>
    /// Command.exe.config entry point:
    ///   appDomainManagerType     = CMOLocalizer.Japanese.JapaneseDomainManager
    ///   appDomainManagerAssembly = CMOLocalizerJa, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
    /// </summary>
    public sealed class JapaneseDomainManager : AppDomainManager
    {
        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            try
            {
                if (AppDomain.CurrentDomain.IsDefaultAppDomain())
                {
                    JapaneseUiLocalization.Start(AppDomain.CurrentDomain.BaseDirectory);
                }
            }
            catch (Exception ex)
            {
                JapaneseUiLocalization.LogException("InitializeNewDomain", ex);
            }
            base.InitializeNewDomain(appDomainInfo);
        }
    }

    public static class JapaneseUiLocalization
    {
        public const string Profile = "ja-minimal-ui-v1";
        public const string DictionaryFileName = "CMOLocalizerJa.json";
        public const string LogFileName = "CMOLocalizerJa.log";
        public const string UntranslatedFileName = "CMOLocalizerJa_untranslated.tsv";

        private const int MaxRewritesPerSecond = 30;
        private const int RewriteCooldownMs = 3000;
        private const int FormRescanMs = 2500;
        private const int MaxMisses = 4000;
        private const long MaxLogBytes = 4L * 1024 * 1024;
        private const int MaxDepth = 64;

        // ── catalog ─────────────────────────────────────────────────────────
        private sealed class Catalog
        {
            internal readonly Dictionary<string, string> Menu;
            internal readonly Dictionary<string, string> Options;
            internal readonly string ProfileName;
            internal readonly string GameBuild;

            internal Catalog(Dictionary<string, string> menu, Dictionary<string, string> options,
                             string profile, string gameBuild)
            {
                Menu = menu;
                Options = options;
                ProfileName = profile;
                GameBuild = gameBuild;
            }
        }

        private static volatile Catalog catalog;
        private static int started;

        // ── diagnostics ─────────────────────────────────────────────────────
        private static readonly object logLock = new object();
        private static string logPath;
        private static string missPath;
        private static long logBytes;
        private static readonly object missLock = new object();
        private static readonly HashSet<string> misses = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> seenForms = new HashSet<string>(StringComparer.Ordinal);
        private static int menuWrites, optionsWrites, eventRewrites, blockedRewrites, missCount, scans;
        private static long slowestScanMs;

        // ── per-object state (weak: never keeps game controls alive) ───────
        private sealed class ItemHook { internal bool Options; internal bool TextOwned; }
        private sealed class WriteGate { internal int WindowStart; internal int Count; internal int BlockedUntil; internal bool Blocked; }
        private sealed class ScanState { internal int LastScan; internal bool Scanned; }
        private static readonly ConditionalWeakTable<ToolStripItem, ItemHook> itemHooks = new ConditionalWeakTable<ToolStripItem, ItemHook>();
        private static readonly ConditionalWeakTable<Control, object> controlHooks = new ConditionalWeakTable<Control, object>();
        private static readonly ConditionalWeakTable<ContextMenuStrip, object> contextHooks = new ConditionalWeakTable<ContextMenuStrip, object>();
        private static readonly ConditionalWeakTable<object, WriteGate> gates = new ConditionalWeakTable<object, WriteGate>();
        private static readonly ConditionalWeakTable<Form, object> optionsForms = new ConditionalWeakTable<Form, object>();
        private static readonly ConditionalWeakTable<Control, ScanState> scanStates = new ConditionalWeakTable<Control, ScanState>();
        private static readonly object Marker = new object();
        private static readonly Dictionary<Type, FieldInfo[]> toolTipFields = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, PropertyInfo> sectionHeaders = new Dictionary<Type, PropertyInfo>();
        private static readonly object reflectionLock = new object();
        [ThreadStatic] private static int writingDepth;

        // Options-only dynamic labels. Japanese text: "{n}秒ごと", "{n}分ごと", "垂直方向の倍率: x{n}".
        private static readonly Regex EveryRule = new Regex(
            @"^Every (\d{1,4}) (second|seconds|minute|minutes)$", RegexOptions.CultureInvariant);
        private static readonly Regex VerticalScalingRule = new Regex(
            @"^Vertical scaling: x(\d{1,3}(?:\.\d{1,2})?)$", RegexOptions.CultureInvariant);
        private const string SecondsSuffix = "秒ごと";
        private const string MinutesSuffix = "分ごと";
        private const string VerticalScalingPrefix = "垂直方向の倍率: x";

        // ── public surface (runtime + isolated fixtures) ────────────────────

        public static void Start(string baseDir)
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            SetDiagnosticsDirectory(baseDir);
            try
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            }
            catch { }
            Log("=== CMOLocalizerJa " + Profile + " starting; pid=" + Process.GetCurrentProcess().Id
                + " assembly=" + typeof(JapaneseUiLocalization).Assembly.GetName().Version);
            string error;
            string dictPath = Path.Combine(baseDir ?? "", DictionaryFileName);
            if (!LoadDictionaryFile(dictPath, out error))
            {
                Log("dictionary rejected (" + error + "); the English UI is left unchanged.");
                return;
            }
            Catalog cat = catalog;
            Log("dictionary loaded: profile=" + cat.ProfileName + " game_build=" + cat.GameBuild
                + " menu=" + cat.Menu.Count + " options=" + cat.Options.Count);
            Thread watcher = new Thread(WatchLoop);
            watcher.IsBackground = true;
            watcher.Name = "CMOLocalizerJa watcher";
            watcher.Start();
        }

        public static void SetDiagnosticsDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                logPath = null;
                missPath = null;
                return;
            }
            logPath = Path.Combine(directory, LogFileName);
            missPath = Path.Combine(directory, UntranslatedFileName);
        }

        public static bool LoadDictionaryFile(string path, out string error)
        {
            try
            {
                if (!File.Exists(path))
                {
                    error = "missing " + path;
                    return false;
                }
                return LoadDictionaryText(File.ReadAllText(path, new UTF8Encoding(false, true)), out error);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Validate and install a dictionary. A rejected dictionary leaves the current one in place.</summary>
        public static bool LoadDictionaryText(string json, out string error)
        {
            error = null;
            try
            {
                Dictionary<string, object> root = MiniJson.Parse(json) as Dictionary<string, object>;
                if (root == null) { error = "top level is not an object"; return false; }
                string[] sections = { "_meta", "comments", "offsets", "exact", "partial", "words" };
                foreach (string name in sections)
                {
                    if (!(GetValue(root, name) is Dictionary<string, object>)) { error = "missing section " + name; return false; }
                }
                if (root.Count != sections.Length) { error = "unexpected top-level sections"; return false; }
                foreach (string empty in new[] { "comments", "offsets", "partial", "words" })
                {
                    if (((Dictionary<string, object>)root[empty]).Count != 0)
                    {
                        error = "section " + empty + " must be empty for the Japanese UI profile";
                        return false;
                    }
                }
                Dictionary<string, object> meta = (Dictionary<string, object>)root["_meta"];
                if (!string.Equals(GetValue(meta, "language") as string, "ja", StringComparison.Ordinal))
                {
                    error = "_meta.language is not ja";
                    return false;
                }
                string profile = GetValue(meta, "profile") as string;
                if (string.IsNullOrEmpty(profile) || !profile.StartsWith("ja-", StringComparison.Ordinal))
                {
                    error = "_meta.profile is not a Japanese profile";
                    return false;
                }
                Dictionary<string, object> exact = (Dictionary<string, object>)root["exact"];
                foreach (KeyValuePair<string, object> pair in exact)
                {
                    string value = pair.Value as string;
                    if (pair.Key.Length == 0 || string.IsNullOrEmpty(value)) { error = "invalid exact entry"; return false; }
                }
                Dictionary<string, object> scopes = GetValue(meta, "scopes") as Dictionary<string, object>;
                if (scopes == null) { error = "_meta.scopes missing"; return false; }
                Dictionary<string, string> menu = BuildScope(scopes, "menu", exact, ref error);
                if (menu == null) return false;
                Dictionary<string, string> options = BuildScope(scopes, "options", exact, ref error);
                if (options == null) return false;
                catalog = new Catalog(menu, options, profile, GetValue(meta, "game_build") as string ?? "");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public static string TranslateMenuText(string text)
        {
            Catalog cat = catalog;
            string translated;
            return cat != null && TryTranslate(cat.Menu, false, text, out translated) ? translated : text;
        }

        public static string TranslateOptionsText(string text)
        {
            Catalog cat = catalog;
            string translated;
            return cat != null && TryTranslate(cat.Options, true, text, out translated) ? translated : text;
        }

        public static bool IsOptionsForm(Form form)
        {
            return form != null && DetectOptionsForm(form);
        }

        /// <summary>Synchronous scan of one window on its UI thread (the watcher schedules the same call).</summary>
        public static void TranslateForm(Form form)
        {
            if (form == null || form.IsDisposed || catalog == null) return;
            Interlocked.Increment(ref scans);
            NoteForm(form);
            if (DetectOptionsForm(form))
            {
                ApplyControlText(form, true);
                TranslateOptionsTree(form, FindToolTips(form), 0);
            }
            else
            {
                WalkMenus(form, 0);
            }
        }

        public static void TranslateToolStripItems(ToolStripItemCollection items)
        {
            if (catalog != null) TranslateItems(items, false, 0);
        }

        public static string Stats()
        {
            return "menuWrites=" + Thread.VolatileRead(ref menuWrites)
                + ";optionsWrites=" + Thread.VolatileRead(ref optionsWrites)
                + ";eventRewrites=" + Thread.VolatileRead(ref eventRewrites)
                + ";blockedRewrites=" + Thread.VolatileRead(ref blockedRewrites)
                + ";misses=" + Thread.VolatileRead(ref missCount)
                + ";scans=" + Thread.VolatileRead(ref scans);
        }

        public static void ResetCounters()
        {
            Interlocked.Exchange(ref menuWrites, 0);
            Interlocked.Exchange(ref optionsWrites, 0);
            Interlocked.Exchange(ref eventRewrites, 0);
            Interlocked.Exchange(ref blockedRewrites, 0);
            Interlocked.Exchange(ref missCount, 0);
            Interlocked.Exchange(ref scans, 0);
        }

        // ── lookup ──────────────────────────────────────────────────────────

        private static Dictionary<string, string> BuildScope(Dictionary<string, object> scopes, string name,
                                                            Dictionary<string, object> exact, ref string error)
        {
            List<object> keys = GetValue(scopes, name) as List<object>;
            if (keys == null || keys.Count == 0) { error = "_meta.scopes." + name + " missing or empty"; return null; }
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (object item in keys)
            {
                string key = item as string;
                object value;
                if (key == null || !exact.TryGetValue(key, out value)) { error = name + " key not in exact"; return null; }
                if (map.ContainsKey(key)) { error = name + " key listed twice"; return null; }
                map.Add(key, (string)value);
            }
            HashSet<string> trimmedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in map.Keys) trimmedKeys.Add(key.Trim());
            foreach (KeyValuePair<string, string> pair in map)
            {
                if (map.ContainsKey(pair.Value) || trimmedKeys.Contains(pair.Value.Trim()))
                {
                    error = name + " value is also a source key (not idempotent): " + pair.Key;
                    return null;
                }
            }
            return map;
        }

        private static object GetValue(Dictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        private static bool TryTranslate(Dictionary<string, string> map, bool optionsScope, string text, out string translated)
        {
            translated = null;
            if (map == null || string.IsNullOrEmpty(text)) return false;
            if (map.TryGetValue(text, out translated)) return true;
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return false;
            string core;
            if (trimmed.Length != text.Length && map.TryGetValue(trimmed, out core))
            {
                int lead = text.Length - text.TrimStart().Length;
                int trail = text.Length - text.TrimEnd().Length;
                translated = text.Substring(0, lead) + core.Trim() + text.Substring(text.Length - trail);
                return true;
            }
            if (!optionsScope) return false;
            Match every = EveryRule.Match(text);
            if (every.Success)
            {
                bool minutes = every.Groups[2].Value.StartsWith("minute", StringComparison.Ordinal);
                translated = every.Groups[1].Value + (minutes ? MinutesSuffix : SecondsSuffix);
                return true;
            }
            Match scaling = VerticalScalingRule.Match(text);
            if (scaling.Success)
            {
                translated = VerticalScalingPrefix + scaling.Groups[1].Value;
                return true;
            }
            return false;
        }

        // ── menus ───────────────────────────────────────────────────────────

        private static void WalkMenus(Control control, int depth)
        {
            if (control == null || depth > MaxDepth || control.IsDisposed) return;
            ContextMenuStrip context = control.ContextMenuStrip;
            if (context != null) HookContextMenu(context);
            ToolStrip strip = control as ToolStrip;
            if (strip != null)
            {
                if (strip is MenuStrip || strip is ToolStripDropDown)
                {
                    TranslateItems(strip.Items, false, 0);
                }
                else if (!(strip is StatusStrip))
                {
                    // Toolbars: buttons stay as they are; their drop-down menus are menus.
                    foreach (ToolStripItem item in Snapshot(strip.Items))
                    {
                        ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
                        if (dropDown == null) continue;
                        HookItem(dropDown, false, false);
                        if (dropDown.HasDropDownItems) TranslateItems(dropDown.DropDownItems, false, 1);
                    }
                }
                return;
            }
            if (!control.HasChildren) return;
            foreach (Control child in Snapshot(control.Controls)) WalkMenus(child, depth + 1);
        }

        private static void HookContextMenu(ContextMenuStrip menu)
        {
            object marker;
            if (contextHooks.TryGetValue(menu, out marker)) return;
            try { contextHooks.Add(menu, Marker); } catch (ArgumentException) { return; }
            menu.Opening += OnContextMenuOpening;
        }

        private static void OnContextMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                ContextMenuStrip menu = sender as ContextMenuStrip;
                if (menu != null && catalog != null) TranslateItems(menu.Items, false, 0);
            }
            catch (Exception ex) { LogException("ContextMenu.Opening", ex); }
        }

        private static void TranslateItems(ToolStripItemCollection items, bool optionsScope, int depth)
        {
            if (items == null || depth > 16) return;
            foreach (ToolStripItem item in Snapshot(items)) TranslateItem(item, optionsScope, depth);
        }

        private static void TranslateItem(ToolStripItem item, bool optionsScope, int depth)
        {
            Catalog cat = catalog;
            if (item == null || item.IsDisposed || cat == null) return;
            Dictionary<string, string> map = optionsScope ? cat.Options : cat.Menu;
            HookItem(item, optionsScope, true);
            if (!(item is ToolStripSeparator))
            {
                string text = item.Text;
                string translated;
                if (TryTranslate(map, optionsScope, text, out translated))
                {
                    WriteItemText(item, translated, optionsScope);
                }
                else if (!optionsScope)
                {
                    RecordMiss("menu", item.GetType().Name, text);
                }
                ToolStripMenuItem menuItem = item as ToolStripMenuItem;
                if (menuItem != null && TryTranslate(map, optionsScope, menuItem.ShortcutKeyDisplayString, out translated))
                {
                    menuItem.ShortcutKeyDisplayString = translated;
                }
                string tip = item.ToolTipText;
                if (!(item.AutoToolTip && tip == item.Text) && TryTranslate(map, optionsScope, tip, out translated))
                {
                    item.ToolTipText = translated;
                }
            }
            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown != null && dropDown.HasDropDownItems)
            {
                TranslateItems(dropDown.DropDownItems, optionsScope, depth + 1);
            }
        }

        /// <param name="textOwned">false for toolbar buttons: only their drop-down menus are translated.</param>
        private static void HookItem(ToolStripItem item, bool optionsScope, bool textOwned)
        {
            ItemHook hook;
            if (itemHooks.TryGetValue(item, out hook))
            {
                if (textOwned) hook.TextOwned = true;
                return;
            }
            hook = new ItemHook();
            hook.Options = optionsScope;
            hook.TextOwned = textOwned;
            try { itemHooks.Add(item, hook); } catch (ArgumentException) { return; }
            item.TextChanged += OnItemTextChanged;
            ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
            if (dropDown != null) dropDown.DropDownOpening += OnDropDownOpening;
        }

        private static void OnItemTextChanged(object sender, EventArgs e)
        {
            if (writingDepth > 0) return;
            try
            {
                ToolStripItem item = sender as ToolStripItem;
                ItemHook hook;
                Catalog cat = catalog;
                if (item == null || cat == null || !itemHooks.TryGetValue(item, out hook) || !hook.TextOwned) return;
                string translated;
                if (!TryTranslate(hook.Options ? cat.Options : cat.Menu, hook.Options, item.Text, out translated)) return;
                if (!AllowRewrite(item, "item:" + item.Name)) return;
                Interlocked.Increment(ref eventRewrites);
                WriteItemText(item, translated, hook.Options);
            }
            catch (Exception ex) { LogException("ToolStripItem.TextChanged", ex); }
        }

        private static void OnDropDownOpening(object sender, EventArgs e)
        {
            try
            {
                ToolStripDropDownItem dropDown = sender as ToolStripDropDownItem;
                ItemHook hook;
                if (dropDown == null || catalog == null || !itemHooks.TryGetValue(dropDown, out hook)) return;
                if (dropDown.HasDropDownItems) TranslateItems(dropDown.DropDownItems, hook.Options, 0);
                // Game handlers that fill the drop-down run in the same event; catch them afterwards.
                ToolStrip owner = dropDown.Owner;
                if (owner != null && owner.IsHandleCreated)
                {
                    bool options = hook.Options;
                    owner.BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            if (!dropDown.IsDisposed && dropDown.HasDropDownItems)
                                TranslateItems(dropDown.DropDownItems, options, 0);
                        }
                        catch (Exception ex) { LogException("DropDownOpening.followUp", ex); }
                    });
                }
            }
            catch (Exception ex) { LogException("DropDownOpening", ex); }
        }

        private static void WriteItemText(ToolStripItem item, string text, bool optionsScope)
        {
            if (item.Text == text) return;
            writingDepth++;
            try { item.Text = text; }
            finally { writingDepth--; }
            if (optionsScope) Interlocked.Increment(ref optionsWrites);
            else Interlocked.Increment(ref menuWrites);
        }

        // ── Game Options window ─────────────────────────────────────────────

        private static bool DetectOptionsForm(Form form)
        {
            object marker;
            if (optionsForms.TryGetValue(form, out marker)) return true;
            string title = (form.Text ?? "").Trim();
            string typeName = form.GetType().Name;
            bool match = title == "Game Options" || title == "Options"
                || string.Equals(typeName, "GameOptions", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Options", StringComparison.OrdinalIgnoreCase);
            if (!match) return false;
            try { optionsForms.Add(form, Marker); } catch (ArgumentException) { }
            Log("Game Options window detected: type=" + form.GetType().FullName + " title=" + title);
            return true;
        }

        private static void TranslateOptionsTree(Control parent, ToolTip[] tips, int depth)
        {
            if (parent == null || depth > MaxDepth || !parent.HasChildren) return;
            foreach (Control child in Snapshot(parent.Controls))
            {
                if (child.IsDisposed) continue;
                TranslateToolTips(child, tips);
                if (IsProtectedControl(child)) continue;
                DataGridView grid = child as DataGridView;
                if (grid != null) { TranslateGridHeaders(grid); continue; }
                ToolStrip strip = child as ToolStrip;
                if (strip != null) { TranslateItems(strip.Items, true, 0); continue; }
                ApplyControlText(child, true);
                ApplySectionHeader(child);
                TranslateOptionsTree(child, tips, depth + 1);
            }
        }

        private static bool IsProtectedControl(Control control)
        {
            if (control is TextBoxBase || control is ComboBox || control is ListControl
                || control is UpDownBase || control is DateTimePicker || control is LinkLabel) return true;
            string name = control.GetType().Name;
            return name.IndexOf("TextBox", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("EditBox", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ComboBox", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("UpDown", StringComparison.OrdinalIgnoreCase) >= 0
                || name.EndsWith("Edit", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyControlText(Control control, bool hook)
        {
            Catalog cat = catalog;
            if (cat == null) return;
            if (hook) HookControl(control);
            string text = control.Text;
            string translated;
            if (TryTranslate(cat.Options, true, text, out translated)) WriteControlText(control, translated);
            else RecordMiss("options", control.GetType().Name, text);
        }

        private static void HookControl(Control control)
        {
            object marker;
            if (controlHooks.TryGetValue(control, out marker)) return;
            try { controlHooks.Add(control, Marker); } catch (ArgumentException) { return; }
            control.TextChanged += OnControlTextChanged;
        }

        private static void OnControlTextChanged(object sender, EventArgs e)
        {
            if (writingDepth > 0) return;
            try
            {
                Control control = sender as Control;
                Catalog cat = catalog;
                string translated;
                if (control == null || cat == null || control.IsDisposed) return;
                if (!TryTranslate(cat.Options, true, control.Text, out translated)) return;
                if (!AllowRewrite(control, "control:" + control.Name)) return;
                Interlocked.Increment(ref eventRewrites);
                WriteControlText(control, translated);
            }
            catch (Exception ex) { LogException("Control.TextChanged", ex); }
        }

        private static void WriteControlText(Control control, string text)
        {
            if (control.Text == text) return;
            writingDepth++;
            try { control.Text = text; }
            finally { writingDepth--; }
            Interlocked.Increment(ref optionsWrites);
        }

        private static void ApplySectionHeader(Control control)
        {
            PropertyInfo property = SectionHeaderProperty(control.GetType());
            if (property == null) return;
            string text = property.GetValue(control, null) as string;
            string translated;
            Catalog cat = catalog;
            if (cat == null || !TryTranslate(cat.Options, true, text, out translated)) return;
            writingDepth++;
            try { property.SetValue(control, translated, null); }
            finally { writingDepth--; }
            Interlocked.Increment(ref optionsWrites);
        }

        private static PropertyInfo SectionHeaderProperty(Type type)
        {
            lock (reflectionLock)
            {
                PropertyInfo property;
                if (sectionHeaders.TryGetValue(type, out property)) return property;
                property = type.GetProperty("SectionHeader", BindingFlags.Instance | BindingFlags.Public);
                if (property != null && (property.PropertyType != typeof(string) || !property.CanRead
                    || !property.CanWrite || property.GetIndexParameters().Length != 0)) property = null;
                sectionHeaders[type] = property;
                return property;
            }
        }

        private static void TranslateGridHeaders(DataGridView grid)
        {
            Catalog cat = catalog;
            if (cat == null) return;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                string translated;
                if (TryTranslate(cat.Options, true, column.HeaderText, out translated) && column.HeaderText != translated)
                {
                    column.HeaderText = translated;
                    Interlocked.Increment(ref optionsWrites);
                }
            }
        }

        private static ToolTip[] FindToolTips(Form form)
        {
            FieldInfo[] fields;
            Type formType = form.GetType();
            lock (reflectionLock)
            {
                if (!toolTipFields.TryGetValue(formType, out fields))
                {
                    List<FieldInfo> found = new List<FieldInfo>();
                    for (Type t = formType; t != null && t != typeof(Form); t = t.BaseType)
                    {
                        foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (typeof(ToolTip).IsAssignableFrom(field.FieldType)) found.Add(field);
                        }
                    }
                    fields = found.ToArray();
                    toolTipFields[formType] = fields;
                }
            }
            List<ToolTip> tips = new List<ToolTip>();
            foreach (FieldInfo field in fields)
            {
                ToolTip tip = field.GetValue(form) as ToolTip;
                if (tip != null && !tips.Contains(tip)) tips.Add(tip);
            }
            return tips.ToArray();
        }

        private static void TranslateToolTips(Control control, ToolTip[] tips)
        {
            Catalog cat = catalog;
            if (cat == null || tips == null) return;
            foreach (ToolTip tip in tips)
            {
                string text = tip.GetToolTip(control);
                string translated;
                if (TryTranslate(cat.Options, true, text, out translated))
                {
                    tip.SetToolTip(control, translated);
                    Interlocked.Increment(ref optionsWrites);
                }
            }
        }

        // ── write-back storm protection ─────────────────────────────────────

        private static bool AllowRewrite(object target, string what)
        {
            WriteGate gate = gates.GetValue(target, delegate(object key) { return new WriteGate(); });
            int now = Environment.TickCount;
            if (gate.Blocked)
            {
                if (now - gate.BlockedUntil < 0) return false;
                gate.Blocked = false;
                gate.Count = 0;
                gate.WindowStart = now;
            }
            if (gate.Count == 0 || now - gate.WindowStart >= 1000 || now - gate.WindowStart < 0)
            {
                gate.WindowStart = now;
                gate.Count = 0;
            }
            gate.Count++;
            if (gate.Count <= MaxRewritesPerSecond) return true;
            gate.Blocked = true;
            gate.BlockedUntil = now + RewriteCooldownMs;
            Interlocked.Increment(ref blockedRewrites);
            Log("write-back storm on " + what + ": paused " + RewriteCooldownMs + " ms (more than "
                + MaxRewritesPerSecond + " rewrites/s)");
            return false;
        }

        // ── watcher (background thread only enumerates and posts) ─────────

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        private static readonly EnumWindowsProc topWindowCallback = OnTopWindow;
        private static readonly object pendingLock = new object();
        private static readonly HashSet<Control> pending = new HashSet<Control>();
        private static uint currentPid;

        private static void WatchLoop()
        {
            currentPid = (uint)Process.GetCurrentProcess().Id;
            Log("watcher started");
            int loop = 0;
            while (true)
            {
                try
                {
                    Thread.Sleep(loop < 60 ? 500 : 1000);
                    loop++;
                    EnumWindows(topWindowCallback, IntPtr.Zero);
                    if (loop == 20 || loop % 300 == 0)
                    {
                        Log("stats " + Stats() + " slowestScanMs=" + Interlocked.Read(ref slowestScanMs));
                    }
                }
                catch (Exception ex)
                {
                    LogException("watcher", ex);
                    Thread.Sleep(2000);
                }
            }
        }

        private static bool OnTopWindow(IntPtr hwnd, IntPtr lParam)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid != currentPid || !IsWindowVisible(hwnd)) return true;
                Control control = Control.FromHandle(hwnd);
                if (control == null || control.IsDisposed) return true;
                if (control is Form || control is ToolStripDropDown) Schedule(control);
            }
            catch { }
            return true;
        }

        private static void Schedule(Control control)
        {
            lock (pendingLock)
            {
                if (pending.Count > 64) pending.RemoveWhere(delegate(Control c) { return c.IsDisposed; });
                if (!pending.Add(control)) return;   // never flood a busy UI thread
            }
            try
            {
                control.BeginInvoke((MethodInvoker)delegate
                {
                    lock (pendingLock) pending.Remove(control);
                    RunScan(control);
                });
            }
            catch
            {
                lock (pendingLock) pending.Remove(control);
            }
        }

        private static void RunScan(Control control)
        {
            try
            {
                if (control.IsDisposed || catalog == null) return;
                Stopwatch watch = Stopwatch.StartNew();
                Form form = control as Form;
                if (form != null)
                {
                    ScanState state = scanStates.GetOrCreateValue(form);
                    int now = Environment.TickCount;
                    bool options = DetectOptionsForm(form);
                    if (!options && state.Scanned && now - state.LastScan >= 0 && now - state.LastScan < FormRescanMs) return;
                    state.Scanned = true;
                    state.LastScan = now;
                    TranslateForm(form);
                }
                else
                {
                    ToolStripDropDown dropDown = control as ToolStripDropDown;
                    if (dropDown != null) TranslateItems(dropDown.Items, false, 0);
                }
                long elapsed = watch.ElapsedMilliseconds;
                if (elapsed > Interlocked.Read(ref slowestScanMs))
                {
                    Interlocked.Exchange(ref slowestScanMs, elapsed);
                    if (elapsed >= 50) Log("slow scan " + elapsed + " ms: " + control.GetType().FullName);
                }
            }
            catch (Exception ex) { LogException("scan", ex); }
        }

        // ── diagnostics ─────────────────────────────────────────────────────

        private static void NoteForm(Form form)
        {
            if (logPath == null) return;
            // Once per window type: CMO's main title changes with the scenario and clock.
            string key = form.GetType().FullName;
            lock (missLock)
            {
                if (seenForms.Count >= 200 || !seenForms.Add(key)) return;
            }
            Log("window: type=" + form.GetType().FullName + " title=" + OneLine(form.Text));
        }

        private static void RecordMiss(string scope, string owner, string text)
        {
            string path = missPath;
            if (path == null || string.IsNullOrEmpty(text)) return;
            string value = text.Trim();
            if (value.Length < 2 || value.Length > 300 || !HasLatinWord(value) || ContainsJapanese(value)) return;
            // Recent-file paths, e-mail-like strings and scenario files are personal, not UI.
            if (value.IndexOf('\\') >= 0 || value.IndexOf(":/", StringComparison.Ordinal) >= 0 || value.IndexOf('@') >= 0
                || value.EndsWith(".scen", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith(".save", StringComparison.OrdinalIgnoreCase)) return;
            lock (missLock)
            {
                if (misses.Count >= MaxMisses || !misses.Add(scope + "\t" + value)) return;
                Interlocked.Increment(ref missCount);
                try
                {
                    File.AppendAllText(path, scope + "\t" + owner + "\t" + OneLine(value) + "\r\n", new UTF8Encoding(false));
                }
                catch { }
            }
        }

        private static bool HasLatinWord(string s)
        {
            int run = 0;
            foreach (char c in s)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                {
                    if (++run >= 2) return true;
                }
                else run = 0;
            }
            return false;
        }

        private static bool ContainsJapanese(string s)
        {
            foreach (char c in s)
            {
                if ((c >= (char)0x3040 && c <= (char)0x30FF) || (c >= (char)0x3400 && c <= (char)0x9FFF)
                    || (c >= (char)0xFF66 && c <= (char)0xFF9F)) return true;
            }
            return false;
        }

        private static string OneLine(string s)
        {
            return (s ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static T[] Snapshot<T>(System.Collections.ICollection items)
        {
            T[] copy = new T[items.Count];
            items.CopyTo(copy, 0);
            return copy;
        }

        private static ToolStripItem[] Snapshot(ToolStripItemCollection items)
        {
            return Snapshot<ToolStripItem>(items);
        }

        private static Control[] Snapshot(Control.ControlCollection controls)
        {
            return Snapshot<Control>(controls);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            string text = ex != null ? ex.ToString() : "(no exception object)";
            if (text.IndexOf("CMOLocalizer.Japanese", StringComparison.Ordinal) >= 0)
            {
                Log("UNHANDLED (engine frame present, terminating=" + e.IsTerminating + "): " + text);
            }
        }

        internal static void LogException(string where, Exception ex)
        {
            Log("ERROR " + where + ": " + (ex != null ? ex.GetType().Name + ": " + ex.Message : "?"));
        }

        private static void Log(string message)
        {
            string path = logPath;
            if (path == null) return;
            lock (logLock)
            {
                if (logBytes > MaxLogBytes) return;
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " " + message + "\r\n";
                try
                {
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                    logBytes += line.Length;
                }
                catch { }
            }
        }
    }

    /// <summary>Strict, dependency-free JSON reader (objects, arrays, strings, numbers, literals).</summary>
    internal static class MiniJson
    {
        private const int MaxNesting = 32;

        internal static object Parse(string text)
        {
            if (text == null) throw new FormatException("no input");
            int index = 0;
            if (text.Length > 0 && text[0] == (char)0xFEFF) index = 1;
            object value = ParseValue(text, ref index, 0);
            SkipWhitespace(text, ref index);
            if (index != text.Length) throw new FormatException("trailing data at " + index);
            return value;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static object ParseValue(string s, ref int i, int depth)
        {
            if (depth > MaxNesting) throw new FormatException("nesting too deep");
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, depth);
            if (c == '[') return ParseArray(s, ref i, depth);
            if (c == '"') return ParseString(s, ref i);
            if (Literal(s, ref i, "true")) return true;
            if (Literal(s, ref i, "false")) return false;
            if (Literal(s, ref i, "null")) return null;
            return ParseNumber(s, ref i);
        }

        private static bool Literal(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i, int depth)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            i++;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return result; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("expected key at " + i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' at " + i);
                i++;
                object value = ParseValue(s, ref i, depth + 1);
                if (result.ContainsKey(key)) throw new FormatException("duplicate key " + key);
                result.Add(key, value);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return result; }
                throw new FormatException("expected ',' or '}' at " + i);
            }
        }

        private static List<object> ParseArray(string s, ref int i, int depth)
        {
            List<object> result = new List<object>();
            i++;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return result; }
            while (true)
            {
                result.Add(ParseValue(s, ref i, depth + 1));
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return result; }
                throw new FormatException("expected ',' or ']' at " + i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++;
            while (true)
            {
                if (i >= s.Length) throw new FormatException("unterminated string");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw new FormatException("control character in string");
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("bad escape");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad unicode escape");
                        int code;
                        if (!int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("bad unicode escape");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        throw new FormatException("bad escape");
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            int digits = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            if (i == digits) throw new FormatException("unexpected character at " + start);
            if (i < s.Length && s[i] == '.')
            {
                i++;
                int fraction = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == fraction) throw new FormatException("bad number at " + start);
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int exponent = i;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i == exponent) throw new FormatException("bad number at " + start);
            }
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}

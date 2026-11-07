// Japanese prose (scope "prose"): scenario descriptions, side briefings and special
// messages rendered by WebView2 in Command.LoadScenario, Command.ChooseSide and
// Command.NewMessageForm. C# 5. Part of CMOLocalizerJa.dll (scripts/build_ja.ps1).
//
// Two round trips per document, both through CoreWebView2.ExecuteScriptAsync (reflection only,
// so the DLL has no compile-time WebView2 dependency):
//   1. collect: the page splits its body into paragraphs exactly like scripts/extract_ja_prose.py
//      (inline runs between block boundaries / <br>, whitespace collapsed) and returns them;
//   2. apply:   the host looks each paragraph up in the prose dictionary and sends back only the
//      hits; the page replaces a paragraph only if it consists of text and pure formatting
//      elements (b, i, span, ...). Links, images, inputs and editable regions are never written.
// Unknown paragraphs stay English and are recorded in CMOLocalizerJa_untranslated.tsv.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CMOLocalizer.Japanese
{
    public static partial class JapaneseUiLocalization
    {
        private const string WebView2TypeName = "Microsoft.Web.WebView2.WinForms.WebView2";
        private const int MaxProseTexts = 400;
        private const int MaxProseLogLines = 40;
        private static readonly HashSet<string> ProseWindowTypes =
            new HashSet<string>(StringComparer.Ordinal) { "LoadScenario", "ChooseSide", "NewMessageForm" };
        private static readonly HashSet<string> notedWebViewHosts = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, FieldInfo[]> webViewFields = new Dictionary<Type, FieldInfo[]>();
        private static int proseWrites, proseRequests, proseLogLines;

        private sealed class ProseState
        {
            internal Control Owner;
            internal string Window;
            internal object Core;
            internal MethodInfo Execute;
            internal bool Hooked;
            internal bool InFlight;
            internal bool Again;
            internal bool WarningLogged;
        }

        private static readonly ConditionalWeakTable<Control, ProseState> proseStates =
            new ConditionalWeakTable<Control, ProseState>();

        /// <summary>Whitespace-normalized lookup in the prose dictionary (same unit as the page script).</summary>
        public static string TranslateProseText(string text)
        {
            Catalog cat = catalog;
            string translated;
            if (cat == null || string.IsNullOrEmpty(text)) return text;
            return cat.Prose.TryGetValue(NormalizeProse(text), out translated) ? translated : text;
        }

        internal static string NormalizeProse(string text)
        {
            StringBuilder sb = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        internal static bool IsProseWindow(Form form)
        {
            return form != null && ProseWindowTypes.Contains(form.GetType().Name);
        }

        /// <summary>Called for every WebView2 met while walking a window, and for WebView2 fields of prose windows.</summary>
        private static void OnWebView(Control view)
        {
            Form form = view.FindForm();
            if (!IsProseWindow(form))
            {
                string host = form != null ? form.GetType().FullName : "(no form)";
                lock (missLock)
                {
                    if (notedWebViewHosts.Count >= 100 || !notedWebViewHosts.Add(host)) return;
                }
                Log("WebView2 in " + host + " is outside the prose scope (not translated)");
                return;
            }
            AttachProse(view, form.GetType().Name);
        }

        private static void AttachProseFields(Form form)
        {
            FieldInfo[] fields;
            Type type = form.GetType();
            lock (reflectionLock)
            {
                if (!webViewFields.TryGetValue(type, out fields))
                {
                    List<FieldInfo> found = new List<FieldInfo>();
                    for (Type t = type; t != null && t != typeof(Form); t = t.BaseType)
                    {
                        foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (field.FieldType.FullName == WebView2TypeName) found.Add(field);
                        }
                    }
                    fields = found.ToArray();
                    webViewFields[type] = fields;
                }
            }
            foreach (FieldInfo field in fields)
            {
                Control view = field.GetValue(form) as Control;
                if (view != null && !view.IsDisposed) AttachProse(view, type.Name);
            }
        }

        private static void AttachProse(Control view, string window)
        {
            Catalog cat = catalog;
            if (cat == null || cat.Prose.Count == 0) return;
            ProseState state = proseStates.GetValue(view, delegate(Control owner)
            {
                ProseState created = new ProseState();
                created.Owner = owner;
                return created;
            });
            state.Window = window;
            if (!state.Hooked)
            {
                state.Hooked = true;
                HookProseEvent(view, "NavigationCompleted", state);
                HookProseEvent(view, "CoreWebView2InitializationCompleted", state);
            }
            RequestProse(state);
        }

        private static void HookProseEvent(object target, string name, ProseState state)
        {
            try
            {
                EventInfo info = target.GetType().GetEvent(name);
                if (info == null) return;
                ParameterInfo[] parameters = info.EventHandlerType.GetMethod("Invoke").GetParameters();
                if (parameters.Length != 2) return;
                Action callback = delegate { RequestProse(state); };
                ParameterExpression sender = Expression.Parameter(parameters[0].ParameterType, "sender");
                ParameterExpression args = Expression.Parameter(parameters[1].ParameterType, "args");
                Delegate handler = Expression.Lambda(info.EventHandlerType,
                    Expression.Invoke(Expression.Constant(callback)), sender, args).Compile();
                info.AddEventHandler(target, handler);
            }
            catch (Exception ex) { LogException("prose hook " + name, ex); }
        }

        private static void RequestProse(ProseState state)
        {
            try
            {
                Catalog cat = catalog;
                Control owner = state.Owner;
                if (cat == null || cat.Prose.Count == 0 || owner.IsDisposed || !owner.IsHandleCreated) return;
                PropertyInfo property = owner.GetType().GetProperty("CoreWebView2");
                object core = property == null ? null : property.GetValue(owner, null);
                if (core == null) return;
                if (!ReferenceEquals(core, state.Core))
                {
                    state.Core = core;
                    state.InFlight = false;
                    state.Execute = core.GetType().GetMethod("ExecuteScriptAsync", new Type[] { typeof(string) });
                    // Earlier than NavigationCompleted: fewer frames of English on screen.
                    HookProseEvent(core, "DOMContentLoaded", state);
                }
                if (state.Execute == null)
                {
                    if (!state.WarningLogged) Log("prose: ExecuteScriptAsync unavailable in " + state.Window);
                    state.WarningLogged = true;
                    return;
                }
                if (state.InFlight) { state.Again = true; return; }
                state.InFlight = true;
                state.Again = false;
                Interlocked.Increment(ref proseRequests);
                RunProseScript(state, core, CollectScript, delegate(string result) { OnProseCollected(state, core, result); });
            }
            catch (Exception ex)
            {
                state.InFlight = false;
                LogException("prose request", ex);
            }
        }

        private static void RunProseScript(ProseState state, object core, string script, Action<string> done)
        {
            Task<string> task = (Task<string>)state.Execute.Invoke(core, new object[] { script });
            task.ContinueWith(delegate(Task<string> completed)
            {
                string failure = completed.IsFaulted ? completed.Exception.GetBaseException().Message
                    : completed.IsCanceled ? "canceled" : null;
                string result = failure == null ? completed.Result : null;
                try
                {
                    state.Owner.BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            if (!ReferenceEquals(core, state.Core)) return;   // a newer CoreWebView2 owns the state
                            if (failure != null)
                            {
                                FinishProse(state);
                                if (!state.WarningLogged) Log("prose: script failed in " + state.Window + ": " + failure);
                                state.WarningLogged = true;
                                return;
                            }
                            done(result);
                        }
                        catch (Exception ex)
                        {
                            FinishProse(state);
                            LogException("prose callback", ex);
                        }
                    });
                }
                catch (InvalidOperationException) { state.InFlight = false; }   // window closed meanwhile
            }, TaskScheduler.Default);
        }

        private static void OnProseCollected(ProseState state, object core, string result)
        {
            Dictionary<string, object> page = string.IsNullOrEmpty(result) ? null : MiniJson.Parse(result) as Dictionary<string, object>;
            List<object> texts = page == null ? null : GetValue(page, "texts") as List<object>;
            string doc = page == null ? null : GetValue(page, "doc") as string;
            Catalog cat = catalog;
            if (texts == null || doc == null || cat == null) { FinishProse(state); return; }
            Dictionary<string, string> hits = new Dictionary<string, string>(StringComparer.Ordinal);
            int misses = 0;
            foreach (object item in texts)
            {
                string text = item as string;
                if (string.IsNullOrEmpty(text) || hits.ContainsKey(text)) continue;
                string translated;
                if (cat.Prose.TryGetValue(NormalizeProse(text), out translated)) hits[text] = translated;
                else if (!ContainsJapanese(text))
                {
                    misses++;
                    RecordMiss("prose", state.Window, text);
                }
            }
            if (hits.Count == 0) { FinishProse(state); return; }
            RunProseScript(state, core, ApplyScript(doc, hits), delegate(string applied)
            {
                int count;
                if (int.TryParse(applied, out count) && count > 0)
                {
                    Interlocked.Add(ref proseWrites, count);
                    if (Interlocked.Increment(ref proseLogLines) <= MaxProseLogLines)
                        Log("prose: " + state.Window + " translated " + count + " paragraph(s), " + misses + " untranslated");
                }
                FinishProse(state);
            });
        }

        private static void FinishProse(ProseState state)
        {
            state.InFlight = false;
            if (state.Again) RequestProse(state);
        }

        private static string ApplyScript(string doc, Dictionary<string, string> hits)
        {
            StringBuilder map = new StringBuilder("{");
            foreach (KeyValuePair<string, string> pair in hits)
            {
                if (map.Length > 1) map.Append(',');
                map.Append(JsString(pair.Key)).Append(':').Append(JsString(pair.Value));
            }
            map.Append('}');
            return ProseLibrary + "return applyCmoJa(" + JsString(doc) + "," + map + ");})()";
        }

        /// <summary>JSON/JS string literal. Prose is whitespace-normalized, so control characters only appear in hostile input.</summary>
        private static string JsString(string value)
        {
            StringBuilder sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ' || c == (char)0x2028 || c == (char)0x2029) sb.Append(' ');
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ES5, no regex escapes that the host has to generate. BLOCK must match scripts/extract_ja_prose.py.
        private const string ProseLibrary = @"(function(){
var BLOCK={ADDRESS:1,ARTICLE:1,ASIDE:1,BLOCKQUOTE:1,BODY:1,CAPTION:1,CENTER:1,DD:1,DIV:1,DL:1,DT:1,FIELDSET:1,
FIGCAPTION:1,FIGURE:1,FOOTER:1,FORM:1,H1:1,H2:1,H3:1,H4:1,H5:1,H6:1,HEADER:1,HR:1,HTML:1,LI:1,MAIN:1,NAV:1,OL:1,
P:1,PRE:1,SECTION:1,TABLE:1,TBODY:1,TD:1,TFOOT:1,TH:1,THEAD:1,TR:1,UL:1};
var SKIP={SCRIPT:1,STYLE:1,HEAD:1,TITLE:1,TEXTAREA:1,SELECT:1,BUTTON:1,NOSCRIPT:1,TEMPLATE:1,INPUT:1,OPTION:1,IFRAME:1};
var FORMAT={B:1,I:1,U:1,EM:1,STRONG:1,SPAN:1,FONT:1,SMALL:1,BIG:1,SUP:1,SUB:1,CODE:1,TT:1,S:1,STRIKE:1,MARK:1,
ABBR:1,CITE:1,Q:1,NOBR:1,WBR:1,INS:1,DEL:1};
var JA=new RegExp('['+String.fromCharCode(0x3040)+'-'+String.fromCharCode(0x30ff)+String.fromCharCode(0x3400)+'-'+String.fromCharCode(0x9fff)+']');
function norm(s){return s.replace(/\s+/g,' ').replace(/^ /,'').replace(/ $/,'');}
function splits(el){var all=el.getElementsByTagName('*');for(var i=0;i<all.length;i++){var t=all[i].tagName.toUpperCase();if(BLOCK[t]||t==='BR'||SKIP[t])return true;}return false;}
function formatOnly(el){if(!FORMAT[el.tagName.toUpperCase()])return false;var all=el.getElementsByTagName('*');for(var i=0;i<all.length;i++){if(!FORMAT[all[i].tagName.toUpperCase()])return false;}return true;}
function paragraphs(root,out){var run=[];
 function flush(){if(!run.length)return;var text='',safe=true;for(var i=0;i<run.length;i++){text+=run[i].textContent;if(run[i].nodeType===1&&!formatOnly(run[i]))safe=false;}
  text=norm(text);if(text&&/[A-Za-z]{2}/.test(text))out.push({nodes:run,text:text,safe:safe});run=[];}
 for(var n=root.firstChild;n;n=n.nextSibling){
  if(n.nodeType===3){run.push(n);continue;}
  if(n.nodeType!==1)continue;
  var tag=n.tagName.toUpperCase();
  if(SKIP[tag]||n.isContentEditable){flush();continue;}
  if(tag==='BR'){flush();continue;}
  if(BLOCK[tag]||splits(n)){flush();paragraphs(n,out);continue;}
  run.push(n);}
 flush();}
function collectCmoJa(){if(!document.body)return null;
 if(!window.__cmoJaDoc)window.__cmoJaDoc=String(Math.random()).slice(2)+String(new Date().getTime());
 var out=[];paragraphs(document.body,out);var seen={},texts=[];
 for(var i=0;i<out.length&&texts.length<" + "400" + @";i++){var t=out[i].text;if(JA.test(t)||seen[t]||t.length>4000)continue;seen[t]=1;texts.push(t);}
 return {doc:window.__cmoJaDoc,texts:texts};}
function applyCmoJa(doc,map){if(!document.body||window.__cmoJaDoc!==doc)return -1;
 var out=[],count=0;paragraphs(document.body,out);
 for(var i=0;i<out.length;i++){var p=out[i];if(!p.safe||!Object.prototype.hasOwnProperty.call(map,p.text))continue;
  var first=p.nodes[0],parent=first.parentNode;if(!parent||parent.isContentEditable)continue;
  parent.insertBefore(document.createTextNode(map[p.text]),first);
  for(var j=0;j<p.nodes.length;j++){if(p.nodes[j].parentNode)p.nodes[j].parentNode.removeChild(p.nodes[j]);}
  count++;}
 return count;}
";

        private static readonly string CollectScript = ProseLibrary + "return collectCmoJa();})()";
    }
}

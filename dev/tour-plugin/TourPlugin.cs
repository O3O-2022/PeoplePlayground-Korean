using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PPGKoreanTour
{
    /// <summary>
    /// 개발용: BepInEx\ppgk_tour.txt 스크립트를 실행해 전/후 비교 스크린샷을 찍는다. (배포본에는 포함하지 않음)
    /// 트리거: BepInEx\ppgk_tour.flag
    /// </summary>
    [BepInPlugin("kr.ppg.koreantour", "PPG Korean Tour (dev)", "0.1.0")]
    public class TourPlugin : BaseUnityPlugin
    {
        private static ManualLogSource Log;
        private string _root, _outDir, _log;
        private bool _running;
        private float _nextCheck;
        private GameObject _hovered;

        private void Awake()
        {
            Log = Logger;
            _root = Paths.BepInExRootPath;
            _outDir = Path.Combine(_root, "tour");
            _log = Path.Combine(_root, "ppgk_tour_log.txt");
        }

        private void Update()
        {
            if (_running || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            string flag = Path.Combine(_root, "ppgk_tour.flag");
            if (!File.Exists(flag)) return;
            File.Delete(flag);
            string script = Path.Combine(_root, "ppgk_tour.txt");
            if (!File.Exists(script)) return;
            Directory.CreateDirectory(_outDir);
            File.WriteAllText(_log, "");
            StartCoroutine(Run(File.ReadAllLines(script)));
        }

        private void Note(string s)
        {
            File.AppendAllText(_log, s + "\n");
        }

        private IEnumerator Run(string[] lines)
        {
            _running = true;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line == "sweep")
                {
                    yield return StartCoroutine(Sweep());
                    Note("OK   sweep");
                    continue;
                }
                int sp = line.IndexOf(' ');
                string cmd = sp < 0 ? line : line.Substring(0, sp);
                string arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                float wait = 0f;
                try
                {
                    wait = Exec(cmd, arg);
                    Note("OK   " + line);
                }
                catch (Exception e)
                {
                    Note("FAIL " + line + " :: " + (e.InnerException ?? e).Message);
                }
                if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
                yield return null;
                yield return null;
            }
            Note("DONE");
            _running = false;
        }

        private float Exec(string cmd, string arg)
        {
            switch (cmd)
            {
                case "wait": return float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                case "lang": SetTranslated(arg == "ko"); return 0.6f;
                case "shot": ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, arg + ".png")); return 0.4f;
                case "click": Click(Find(arg, true)); return 0.3f;
                case "hover": Hover(Find(arg, false)); return 0.3f;
                case "unhover": Unhover(); return 0.1f;
                case "spawn": Spawn(arg, false); return 0.3f;
                case "spawnc": Spawn(arg, true); return 0.3f;
                case "screen": ScreenOf(arg); return 0f;
                case "category":
                {
                    var cats = FindObjectsOfType<Button>().Where(b => b.name == "CategoryButton(Clone)").OrderBy(b => b.transform.GetSiblingIndex()).ToList();
                    cats[int.Parse(arg)].onClick.Invoke();
                    return 0.4f;
                }
                case "unpause":
                {
                    Type gt = GameType("Global");
                    object g = Instance(gt);
                    if ((bool)gt.GetProperty("Paused").GetValue(g, null)) gt.GetMethod("TogglePaused").Invoke(g, null);
                    return 0.3f;
                }
                case "scrollmenu":
                {
                    Type cmT = GameType("ContextMenuBehaviour");
                    Component cm = Resources.FindObjectsOfTypeAll(cmT).Cast<Component>().First(x => x.gameObject.scene.IsValid());
                    var sr = cm.GetComponentInChildren<ScrollRect>(true);
                    Canvas.ForceUpdateCanvases();
                    sr.verticalNormalizedPosition = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    return 0.2f;
                }
                case "reload":
                {
                    Type t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin")).First(x => x != null);
                    object cur = t.GetField("Current", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);
                    t.GetMethod("ReloadTranslations", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cur, null);
                    return 1f;
                }
                case "select": Select(arg); return 0.2f;
                case "selectnone": Call("SelectionController", "ClearSelection", ""); return 0.1f;
                case "context": Context(arg); return 0.3f;
                case "camera": CameraTo(arg); return 0.3f;
                case "pause": Call("Global", "SetPausedMenu", arg); return 0.3f;
                case "call": { var p = arg.Split(new[] { ' ' }, 3); Call(p[0], p[1], p.Length > 2 ? p[2] : ""); return 0.3f; }
                case "buttons": DumpButtons(arg); return 0f;
                case "texts": DumpTexts(arg); return 0f;
                default: throw new Exception("unknown command " + cmd);
            }
        }

        // ── XUnity 번역 켜기/끄기 (ALT+T와 같은 동작) ──
        private static void SetTranslated(bool on)
        {
            Type t = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin")).First(x => x != null);
            object cur = t.GetField("Current", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);
            bool now = (bool)t.GetField("_isInTranslatedMode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cur);
            if (now != on) t.GetMethod("ToggleTranslation", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cur, null);
        }

        // ── UI 찾기: "name:오브젝트이름" 또는 화면 글자(태그 제외, 부분 일치는 ~접두사) ──
        private static string Plain(string s)
        {
            return Regex.Replace(s ?? "", "<[^>]*>", "").Trim();
        }

        private static bool TextMatches(GameObject go, string want, bool contains)
        {
            foreach (TMP_Text t in go.GetComponentsInChildren<TMP_Text>(false))
            {
                string p = Plain(t.text);
                if (contains ? p.Contains(want) : p == want) return true;
            }
            foreach (Text t in go.GetComponentsInChildren<Text>(false))
            {
                string p = Plain(t.text);
                if (contains ? p.Contains(want) : p == want) return true;
            }
            return false;
        }

        private static GameObject Find(string arg, bool preferSelectable)
        {
            int index = 0;
            var m = Regex.Match(arg, @"^(.*)#(\d+)$");
            if (m.Success) { arg = m.Groups[1].Value; index = int.Parse(m.Groups[2].Value); }
            var hits = new List<GameObject>();
            if (arg.StartsWith("name:"))
            {
                string n = arg.Substring(5);
                foreach (Transform tr in Resources.FindObjectsOfTypeAll<Transform>())
                    if (tr.gameObject.activeInHierarchy && tr.name == n) hits.Add(tr.gameObject);
            }
            else
            {
                bool contains = arg.StartsWith("~");
                string want = contains ? arg.Substring(1) : arg;
                if (preferSelectable)
                    foreach (Selectable s in FindObjectsOfType<Selectable>())
                        if (s.IsActive() && TextMatches(s.gameObject, want, contains)) hits.Add(s.gameObject);
                if (hits.Count == 0)
                    foreach (TMP_Text t in FindObjectsOfType<TMP_Text>())
                    {
                        string p = Plain(t.text);
                        if (contains ? p.Contains(want) : p == want) hits.Add(t.gameObject);
                    }
            }
            if (hits.Count <= index) throw new Exception("not found: " + arg + " (" + hits.Count + ")");
            return hits[index];
        }

        private static PointerEventData Ped()
        {
            return new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        }

        private static void Click(GameObject go)
        {
            var b = go.GetComponentInParent<Button>();
            if (b != null) { b.onClick.Invoke(); return; }
            var tg = go.GetComponentInParent<UnityEngine.UI.Toggle>();
            if (tg != null) { tg.isOn = !tg.isOn; return; }
            ExecuteEvents.ExecuteHierarchy(go, Ped(), ExecuteEvents.pointerClickHandler);
        }

        private void Hover(GameObject go)
        {
            Unhover();
            ExecuteEvents.ExecuteHierarchy(go, Ped(), ExecuteEvents.pointerEnterHandler);
            _hovered = go;
        }

        private void Unhover()
        {
            if (_hovered != null) ExecuteEvents.ExecuteHierarchy(_hovered, Ped(), ExecuteEvents.pointerExitHandler);
            _hovered = null;
        }

        // ── 게임 쪽 ──
        private static Type GameType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "Assembly-CSharp")
                .Select(a => a.GetType(name)).First(x => x != null);
        }

        private static object Instance(Type t)
        {
            foreach (string n in new[] { "Main", "main", "Instance" })
            {
                var f = t.GetField(n, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.GetValue(null) != null) return f.GetValue(null);
                var p = t.GetProperty(n, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.GetValue(null, null) != null) return p.GetValue(null, null);
            }
            return FindObjectOfType(t);
        }

        private static object Parse(string s, Type t)
        {
            if (t == typeof(bool)) return s == "true";
            if (t == typeof(int)) return int.Parse(s);
            if (t == typeof(float)) return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            return s;
        }

        private static void Call(string typeName, string method, string args)
        {
            Type t = GameType(typeName);
            string[] a = args.Length == 0 ? new string[0] : args.Split(' ');
            MethodInfo mi = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .First(x => x.Name == method && x.GetParameters().Length >= a.Length &&
                            x.GetParameters().Skip(a.Length).All(p => p.IsOptional));
            var ps = mi.GetParameters();
            var vals = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++) vals[i] = i < a.Length ? Parse(a[i], ps[i].ParameterType) : ps[i].DefaultValue;
            mi.Invoke(mi.IsStatic ? null : Instance(t), vals);
        }

        private static readonly List<GameObject> Spawned = new List<GameObject>();

        // screen <오브젝트 이름>: 그 오브젝트의 화면 좌표를 로그에 남긴다 (우클릭 메뉴 위치 잡기용)
        private void ScreenOf(string name)
        {
            GameObject root = FindObjectsOfType<Transform>().Where(t => t.parent == null && t.name == name).Select(t => t.gameObject).LastOrDefault();
            Vector3 sp = Camera.main.WorldToScreenPoint(root.transform.position);
            Note("SCREEN " + name + " " + sp.x.ToString("0") + " " + sp.y.ToString("0") + " cam=" + Camera.main.transform.position);
        }

        private static void Spawn(string arg, bool relative)
        {
            // spawn <x> <y> <아이템 이름>   (spawnc: 카메라 중심 기준 상대 좌표)
            var p = arg.Split(new[] { ' ' }, 3);
            float x = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
            float y = float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture);
            if (relative) { x += Camera.main.transform.position.x; y += Camera.main.transform.position.y; }
            Type catT = GameType("CatalogBehaviour");
            object cat = Instance(catT);
            Type assetT = GameType("SpawnableAsset");
            UnityEngine.Object asset = Resources.FindObjectsOfTypeAll(assetT).First(o => o.name == p[2]);
            catT.GetMethod("SetItem").Invoke(cat, new object[] { asset });
            Type globalT = GameType("Global");
            object global = Instance(globalT);
            globalT.GetField("MousePosition").SetValue(global, new Vector3(x, y, 0f));
            MethodInfo spawn = catT.GetMethod("Spawn", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { assetT, typeof(bool) }, null);
            spawn.Invoke(cat, new object[] { asset, false });
        }

        private static void Select(string arg)
        {
            // select <오브젝트 이름> [자식 이름]  — 이름이 같은 가장 최근 생성물
            var p = arg.Split(new[] { ' ' }, 2);
            GameObject root = FindObjectsOfType<Transform>().Where(t => t.parent == null && t.name == p[0]).Select(t => t.gameObject).LastOrDefault();
            if (root == null) throw new Exception("no object " + p[0]);
            GameObject target = root;
            if (p.Length > 1)
            {
                Transform child = root.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == p[1]);
                if (child == null) throw new Exception("no child " + p[1]);
                target = child.gameObject;
            }
            Type physT = GameType("PhysicalBehaviour");
            var phys = (p.Length > 1 ? target.GetComponents(physT) : target.GetComponentsInChildren(physT)).Cast<object>().ToList();
            Type selT = GameType("SelectionController");
            object sel = Instance(selT);
            selT.GetMethod("ClearSelection").Invoke(sel, null);
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(physT));
            foreach (object o in phys) list.Add(o);
            selT.GetMethods().First(m => m.Name == "Select" && m.GetParameters()[0].ParameterType != physT).Invoke(sel, new object[] { list, false });
        }

        private static void SelectObject(GameObject root)
        {
            Type physT = GameType("PhysicalBehaviour");
            Type selT = GameType("SelectionController");
            object sel = Instance(selT);
            selT.GetMethod("ClearSelection").Invoke(sel, null);
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(physT));
            foreach (object o in root.GetComponentsInChildren(physT)) list.Add(o);
            selT.GetMethods().First(m => m.Name == "Select" && m.GetParameters()[0].ParameterType != physT).Invoke(sel, new object[] { list, false });
        }

        private static void Context(string arg)
        {
            var p = arg.Split(' ');
            Type t = GameType("ContextMenuBehaviour");
            object cm = Resources.FindObjectsOfTypeAll(t).Cast<Component>().First(c => c.gameObject.scene.IsValid());
            t.GetMethod("Show").Invoke(cm, new object[] { new Vector2(float.Parse(p[0]), float.Parse(p[1])) });
        }

        private static void CameraTo(string arg)
        {
            // camera <x> <y> <크기>
            var p = arg.Split(' ').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Camera cam = Camera.main;
            cam.transform.position = new Vector3(p[0], p[1], cam.transform.position.z);
            cam.orthographicSize = p[2];
            var ctl = FindObjectOfType(GameType("CameraControlBehaviour"));
            if (ctl != null)
            {
                var f = ctl.GetType().GetField("targetZoom", BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null) f.SetValue(ctl, p[2]);
            }
        }

        private void DumpButtons(string file)
        {
            var lines = new List<string>();
            foreach (Selectable s in FindObjectsOfType<Selectable>())
            {
                if (!s.IsActive()) continue;
                string txt = string.Join(" / ", s.GetComponentsInChildren<TMP_Text>(false).Select(t => Plain(t.text)).Where(x => x.Length > 0).ToArray());
                lines.Add(s.GetType().Name + "\t" + PathOf(s.transform) + "\t" + txt);
            }
            File.WriteAllText(Path.Combine(_outDir, (file.Length > 0 ? file : "buttons") + ".txt"), string.Join("\n", lines.ToArray()));
        }

        private void DumpTexts(string file)
        {
            var lines = FindObjectsOfType<TMP_Text>().Select(t => PathOf(t.transform) + "\t" + Plain(t.text).Replace("\n", "\\n")).ToArray();
            File.WriteAllText(Path.Combine(_outDir, (file.Length > 0 ? file : "texts") + ".txt"), string.Join("\n", lines));
        }

        // ── 전수 조사: 실제 화면에 표시되는 툴팁/우클릭 메뉴 글자를 모두 모은다 ──
        private readonly List<string> _sweep = new List<string>();

        private void Rec(string kind, string where, string shown)
        {
            _sweep.Add(kind + "\t" + where + "\t" + (shown ?? "").Replace("\r", "\\r").Replace("\n", "\\n"));
        }

        private static string TooltipOf(GameObject go)
        {
            Type ht = GameType("HasTooltipBehaviour");
            Component h = go.GetComponentInChildren(ht, true) ?? go.GetComponentInParent(ht);
            if (h != null)
            {
                var tt = ht.GetField("TooltipText").GetValue(h) as TMP_Text;
                if (tt != null) return tt.text;
            }
            // 아이템 버튼은 별도 툴팁 컴포넌트를 쓸 수 있으므로 활성 툴팁 텍스트를 찾는다
            return null;
        }

        private IEnumerator HoverRead(GameObject go, string kind, string where)
        {
            ExecuteEvents.ExecuteHierarchy(go, Ped(), ExecuteEvents.pointerEnterHandler);
            yield return null;
            string shown = TooltipOf(go);
            if (shown == null)
            {
                // 활성화된 툴팁 오브젝트 중 방금 켜진 것
                shown = string.Join(" || ", FindObjectsOfType<TMP_Text>().Where(t => t.transform.parent != null && t.gameObject.name.ToLower().Contains("tooltip")).Select(t => t.text).ToArray());
            }
            Rec(kind, where, shown);
            ExecuteEvents.ExecuteHierarchy(go, Ped(), ExecuteEvents.pointerExitHandler);
            yield return null;
        }

        private IEnumerator Sweep()
        {
            _sweep.Clear();
            // 1) 카테고리와 아이템 툴팁
            var cats = FindObjectsOfType<Button>().Where(b => b.name == "CategoryButton(Clone)").OrderBy(b => b.transform.GetSiblingIndex()).ToList();
            for (int c = 0; c < cats.Count; c++)
            {
                yield return StartCoroutine(HoverRead(cats[c].gameObject, "category", "#" + c));
                cats[c].onClick.Invoke();
                yield return null; yield return null;
                var items = FindObjectsOfType<Button>().Where(b => b.name == "ItemButton(Clone)").OrderBy(b => b.transform.GetSiblingIndex()).ToList();
                foreach (var it in items)
                {
                    string label = string.Join("/", it.GetComponentsInChildren<TMP_Text>(true).Select(t => Plain(t.text)).ToArray());
                    yield return StartCoroutine(HoverRead(it.gameObject, "item", "cat" + c + ":" + label));
                }
            }
            // 2) 도구 / 능력
            foreach (string tab in new[] { "tab-tools", "tab-powers" })
            {
                var tb = FindObjectsOfType<Button>().FirstOrDefault(b => b.name == tab);
                if (tb != null) { tb.onClick.Invoke(); yield return null; yield return null; }
                foreach (var b in FindObjectsOfType<Button>().Where(b => b.transform.parent != null && (b.transform.parent.name == "Tools" || b.transform.parent.name == "Powers")).ToList())
                    yield return StartCoroutine(HoverRead(b.gameObject, "tool", tab + ":" + b.name));
            }
            var back = FindObjectsOfType<Button>().FirstOrDefault(b => b.name == "tab-tools");
            if (back != null) back.onClick.Invoke();
            // 3) 아이템마다 우클릭 메뉴
            Type assetT = GameType("SpawnableAsset");
            Type cmT = GameType("ContextMenuBehaviour");
            Component cm = Resources.FindObjectsOfTypeAll(cmT).Cast<Component>().First(x => x.gameObject.scene.IsValid());
            var assets = Resources.FindObjectsOfTypeAll(assetT).Where(a => ((ScriptableObject)a).name.Length > 0).OrderBy(a => a.name).ToList();
            foreach (UnityEngine.Object a in assets)
            {
                string err = null;
                GameObject spawned = null;
                try
                {
                    Spawn("0 3 " + a.name, true);
                    spawned = FindObjectsOfType<Transform>().Where(t => t.parent == null && t.name == a.name).Select(t => t.gameObject).LastOrDefault();
                    if (spawned != null) SelectObject(spawned);
                }
                catch (Exception e) { err = (e.InnerException ?? e).Message; }
                if (err != null || spawned == null) { Rec("spawnfail", a.name, err); continue; }
                yield return null;
                try { cmT.GetMethod("Show").Invoke(cm, new object[] { new Vector2(900, 600) }); }
                catch (Exception e) { Rec("menufail", a.name, (e.InnerException ?? e).Message); }
                yield return null; yield return null;
                var btns = cm.GetComponentsInChildren<Button>(false).ToList();
                foreach (var b in btns)
                {
                    string label = string.Join("/", b.GetComponentsInChildren<TMP_Text>(false).Select(t => t.text).ToArray());
                    Rec("menu", a.name, label);
                    yield return StartCoroutine(HoverRead(b.gameObject, "menudesc", a.name + ":" + Plain(label)));
                }
                try { cmT.GetMethod("Hide").Invoke(cm, null); } catch { }
                Call("SelectionController", "ClearSelection", "");
                UnityEngine.Object.Destroy(spawned);
                yield return null;
            }
            File.WriteAllText(Path.Combine(_outDir, "sweep.tsv"), string.Join("\n", _sweep.ToArray()));
        }

        private static string PathOf(Transform t)
        {
            string p = t.name;
            for (int i = 0; t.parent != null && i < 4; i++) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}

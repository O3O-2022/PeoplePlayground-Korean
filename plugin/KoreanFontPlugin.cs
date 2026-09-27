using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace PPGKorean
{
    /// <summary>
    /// TextMeshPro 3.0(Unity 2020.3)은 OS 폰트를 직접 불러오지 못하므로,
    /// 표식용 Font 객체를 만들고 FontEngine.LoadFontFace(Font)를 가로채 malgun.ttf 바이트를 대신 넘긴다.
    /// 이렇게 만든 동적 TMP 폰트를 모든 TMP 폰트의 대체(fallback) 폰트로 등록한다.
    /// </summary>
    [BepInPlugin("kr.ppg.koreanfont", "PPG Korean Font", "1.0.0")]
    public class KoreanFontPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static Font _marker;
        private static byte[] _fontBytes;
        private static TMP_FontAsset _koreanFont;
        private static readonly HashSet<int> Patched = new HashSet<int>();

        private void Awake()
        {
            Log = Logger;
            string fontDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            string[] candidates = { "malgun.ttf", "NanumGothic.ttf", "gulim.ttc" };
            foreach (string name in candidates)
            {
                string path = Path.Combine(fontDir, name);
                if (File.Exists(path))
                {
                    _fontBytes = File.ReadAllBytes(path);
                    Log.LogInfo("한글 폰트 사용: " + path);
                    break;
                }
            }
            if (_fontBytes == null)
            {
                Log.LogError("한글 폰트를 찾지 못했습니다 (malgun.ttf).");
                return;
            }

            _marker = new Font("KoreanFallbackMarker");
            DontDestroyOnLoad(_marker);
            Harmony.CreateAndPatchAll(typeof(KoreanFontPlugin), "kr.ppg.koreanfont");
            SceneManager.sceneLoaded += (s, m) => RegisterFallbacks();
        }

        private void Start()
        {
            RegisterFallbacks();
            _collect = Config.Bind("Debug", "CollectUntranslated", true,
                "화면에 표시됐지만 번역되지 않은 영어 문장을 BepInEx\\PPGKorean_untranslated.txt에 모읍니다.").Value;
            _collectPath = Path.Combine(Paths.BepInExRootPath, "PPGKorean_untranslated.txt");
            if (_collect && File.Exists(_collectPath))
                foreach (string line in File.ReadAllLines(_collectPath)) _seen.Add(line);
        }

        private static readonly HashSet<string> _seen = new HashSet<string>();
        private static readonly System.Text.RegularExpressions.Regex Latin = new System.Text.RegularExpressions.Regex("[A-Za-z]{3}");
        private static readonly System.Text.RegularExpressions.Regex Hangul = new System.Text.RegularExpressions.Regex("[\uAC00-\uD7A3]");
        private bool _collect;
        private string _collectPath;
        private float _nextScan;

        // 한 번 스캔에서 보인 미번역 후보. 다음 스캔(3초 뒤)에도 그대로 남아 있을 때만 기록한다.
        private HashSet<string> _pending = new HashSet<string>();

        private void Update()
        {
            CheckDebugRequest();
            if (!_collect || Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 3f;
            try
            {
                var now = new HashSet<string>();
                foreach (TMP_Text t in FindObjectsOfType<TMP_Text>()) Consider(t.text, now);
                foreach (UnityEngine.UI.Text t in FindObjectsOfType<UnityEngine.UI.Text>()) Consider(t.text, now);
                var fresh = new List<string>();
                foreach (string line in now)
                    if (_pending.Contains(line) && _seen.Add(line)) fresh.Add(line);
                _pending = now;
                if (fresh.Count > 0) File.AppendAllText(_collectPath, string.Join("\n", fresh.ToArray()) + "\n");
            }
            catch (Exception e)
            {
                Log.LogWarning("미번역 수집 중 오류: " + e.Message);
                _collect = false;
            }
        }

        private static string Flatten(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static void Consider(string text, HashSet<string> now)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 2000) return;
            if (!Latin.IsMatch(text) || Hangul.IsMatch(text)) return;
            now.Add(Flatten(text));
        }

        // BepInEx\ppgk_debug.flag 파일이 생기면 게임 화면만 캡처하고, 지금 보이는 모든 텍스트를 저장한 뒤 플래그를 지운다.
        private float _nextFlagCheck;

        private static string Unflatten(string line)
        {
            var sb = new System.Text.StringBuilder(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    char n = line[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == 't') sb.Append('\t');
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Judge(string source, string result)
        {
            if (result == null) return "FAIL";
            if (result == source) return "SAME";
            // 태그를 뺀 번역 결과에 영어 단어(3글자 이상)가 남아 있으면 부분 번역
            string plain = System.Text.RegularExpressions.Regex.Replace(result, "<[^>]*>", "");
            return Latin.IsMatch(plain) && !Hangul.IsMatch(plain) ? "FAIL" : (Latin.IsMatch(plain) ? "PART" : "OK");
        }

        // BepInEx\ppgk_selftest.flag: ppgk_selftest_in.txt의 각 줄과 게임 안 모든 텍스트 컴포넌트(비활성 포함)를
        // XUnity 번역기에 넣어 보고 결과를 ppgk_selftest_out.txt / ppgk_scan_out.txt로 저장한다.
        private static void RunSelfTest()
        {
            var tr = XUnity.AutoTranslator.Plugin.Core.AutoTranslator.Default;
            string root = Paths.BepInExRootPath;
            string input = Path.Combine(root, "ppgk_selftest_in.txt");
            var outLines = new List<string>();
            if (File.Exists(input))
            {
                foreach (string raw in File.ReadAllLines(input))
                {
                    if (raw.Length == 0) continue;
                    string src = Unflatten(raw);
                    string res;
                    bool ok = tr.TryTranslate(src, out res);
                    outLines.Add(Judge(src, ok ? res : null) + "\t" + raw + "\t" + (ok ? Flatten(res) : ""));
                }
            }
            File.WriteAllText(Path.Combine(root, "ppgk_selftest_out.txt"), string.Join("\n", outLines.ToArray()));

            var scan = new List<string>();
            var done = new HashSet<string>();
            var comps = new List<KeyValuePair<Component, string>>();
            foreach (TMP_Text t in Resources.FindObjectsOfTypeAll<TMP_Text>()) comps.Add(new KeyValuePair<Component, string>(t, t.text));
            foreach (UnityEngine.UI.Text t in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>()) comps.Add(new KeyValuePair<Component, string>(t, t.text));
            foreach (var kv in comps)
            {
                string text = kv.Value;
                if (string.IsNullOrEmpty(text) || !Latin.IsMatch(text) || Hangul.IsMatch(text) || !done.Add(text)) continue;
                string res;
                bool ok = tr.TryTranslate(text, out res);
                string path = kv.Key.gameObject.name;
                Transform p = kv.Key.transform.parent;
                for (int d = 0; p != null && d < 3; d++, p = p.parent) path = p.name + "/" + path;
                scan.Add(Judge(text, ok ? res : null) + "\t" + path + "\t" + Flatten(text) + "\t" + (ok ? Flatten(res) : ""));
            }
            File.WriteAllText(Path.Combine(root, "ppgk_scan_out.txt"), string.Join("\n", scan.ToArray()));
            Log.LogInfo("자가 검사 완료: 입력 " + outLines.Count + "줄, 게임 텍스트 " + scan.Count + "개");
        }

        private void CheckDebugRequest()
        {
            if (Time.unscaledTime < _nextFlagCheck) return;
            _nextFlagCheck = Time.unscaledTime + 1f;
            string testFlag = Path.Combine(Paths.BepInExRootPath, "ppgk_selftest.flag");
            if (File.Exists(testFlag))
            {
                try { File.Delete(testFlag); RunSelfTest(); }
                catch (Exception e) { Log.LogWarning("자가 검사 실패: " + e); }
            }
            string flag = Path.Combine(Paths.BepInExRootPath, "ppgk_debug.flag");
            if (!File.Exists(flag)) return;
            try
            {
                File.Delete(flag);
                string stamp = DateTime.Now.ToString("HHmmss");
                ScreenCapture.CaptureScreenshot(Path.Combine(Paths.BepInExRootPath, "ppgk_shot_" + stamp + ".png"));
                var lines = new List<string>();
                foreach (TMP_Text t in FindObjectsOfType<TMP_Text>())
                    if (!string.IsNullOrEmpty(t.text)) lines.Add("[TMP] " + t.gameObject.name + " | " + Flatten(t.text));
                foreach (UnityEngine.UI.Text t in FindObjectsOfType<UnityEngine.UI.Text>())
                    if (!string.IsNullOrEmpty(t.text)) lines.Add("[UGUI] " + t.gameObject.name + " | " + Flatten(t.text));
                File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "ppgk_texts_" + stamp + ".txt"), string.Join("\n", lines.ToArray()));
                Log.LogInfo("디버그 덤프 저장: " + stamp);
            }
            catch (Exception e)
            {
                Log.LogWarning("디버그 덤프 실패: " + e.Message);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FontEngine), "LoadFontFace", new[] { typeof(Font), typeof(int) })]
        private static bool LoadFontFaceSized(Font font, int pointSize, ref FontEngineError __result)
        {
            if (_marker == null || !ReferenceEquals(font, _marker)) return true;
            __result = FontEngine.LoadFontFace(_fontBytes, pointSize);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FontEngine), "LoadFontFace", new[] { typeof(Font) })]
        private static bool LoadFontFacePlain(Font font, ref FontEngineError __result)
        {
            if (_marker == null || !ReferenceEquals(font, _marker)) return true;
            __result = FontEngine.LoadFontFace(_fontBytes);
            return false;
        }

        private static void EnsureFont()
        {
            if (_koreanFont != null || _fontBytes == null) return;
            try
            {
                _koreanFont = TMP_FontAsset.CreateFontAsset(_marker, 72, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                if (_koreanFont == null)
                {
                    Log.LogError("TMP 한글 폰트 생성 실패");
                    return;
                }
                _koreanFont.name = "Malgun Gothic SDF (Korean fallback)";
                DontDestroyOnLoad(_koreanFont);
                Log.LogInfo("TMP 한글 대체 폰트를 만들었습니다.");
            }
            catch (Exception e)
            {
                Log.LogError("TMP 한글 폰트 생성 중 오류: " + e);
            }
        }

        private static void RegisterFallbacks()
        {
            EnsureFont();
            if (_koreanFont == null) return;
            try
            {
                if (TMP_Settings.instance != null)
                {
                    // 한글을 글자 단위가 아니라 띄어쓰기 단위로 줄바꿈
                    TMP_Settings.useModernHangulLineBreakingRules = true;
                    List<TMP_FontAsset> global = TMP_Settings.fallbackFontAssets;
                    if (global != null && !global.Contains(_koreanFont)) global.Add(_koreanFont);
                }
                int added = 0;
                foreach (TMP_FontAsset fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || fa == _koreanFont) continue;
                    if (Patched.Contains(fa.GetInstanceID())) continue;
                    if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    if (!fa.fallbackFontAssetTable.Contains(_koreanFont)) fa.fallbackFontAssetTable.Add(_koreanFont);
                    Patched.Add(fa.GetInstanceID());
                    added++;
                }
                if (added > 0) Log.LogInfo("TMP 폰트 " + added + "개에 한글 대체 폰트를 연결했습니다.");
            }
            catch (Exception e)
            {
                Log.LogError("대체 폰트 등록 중 오류: " + e);
            }
        }
    }
}

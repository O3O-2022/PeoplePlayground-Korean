using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PPGKorean
{
    /// <summary>
    /// 그림(텍스처) 속 글자 한글화.
    /// BepInEx\Translation\ko\Image\index.txt에 적힌 조각 이미지를, 이름과 크기가 같은 게임 그림 위에 덮어씌운다.
    /// 게임 원본 그림을 통째로 배포하지 않으려고 바뀐 부분만 조각으로 갖고 있고, 실행 중에 원본과 합친다.
    /// 조각마다 원본 확인 이미지가 있어서, 게임 업데이트로 그림이 바뀌었으면 그 조각은 건너뛴다.
    /// </summary>
    internal sealed class TextureTranslator
    {
        private sealed class Patch
        {
            public string File;
            public int X, Y, W, H;   // Y는 그림 위쪽 기준 (index.txt와 같음)
            public Color32[] Pixels; // 유니티 순서: 아래쪽 행부터
            public Color32[] Check;
        }

        private sealed class Target
        {
            public int Width, Height;
            public readonly List<Patch> Patches = new List<Patch>();
        }

        private sealed class Applied
        {
            public Texture2D Texture;
            public Color32[] Original, Translated;
        }

        private readonly Dictionary<string, Target> _targets = new Dictionary<string, Target>();
        private readonly Dictionary<int, Applied> _applied = new Dictionary<int, Applied>();
        private readonly HashSet<int> _skipped = new HashSet<int>();
        private readonly HashSet<string> _warned = new HashSet<string>();
        private bool _translated = true;

        public int PatchCount { get; private set; }

        public static TextureTranslator Load(string dir)
        {
            var tt = new TextureTranslator();
            string index = Path.Combine(dir, "index.txt");
            if (!File.Exists(index)) return tt;
            foreach (string raw in File.ReadAllLines(index))
            {
                if (raw.Length == 0 || raw.StartsWith("#")) continue;
                string[] f = raw.Split('\t');
                if (f.Length < 7) continue;
                try
                {
                    var p = new Patch { File = f[5], X = int.Parse(f[3]), Y = int.Parse(f[4]) };
                    int w, h;
                    p.Pixels = ReadPng(Path.Combine(dir, f[5]), out p.W, out p.H);
                    p.Check = ReadPng(Path.Combine(dir, f[6]), out w, out h);
                    if (w != p.W || h != p.H) throw new InvalidDataException("원본 확인 이미지 크기가 다릅니다");
                    Target t;
                    if (!tt._targets.TryGetValue(f[0], out t))
                        tt._targets[f[0]] = t = new Target { Width = int.Parse(f[1]), Height = int.Parse(f[2]) };
                    t.Patches.Add(p);
                    tt.PatchCount++;
                }
                catch (Exception e)
                {
                    KoreanFontPlugin.Log.LogWarning("그림 조각을 읽지 못했습니다 (" + f[5] + "): " + e.Message);
                }
            }
            return tt;
        }

        private static Color32[] ReadPng(string path, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) throw new InvalidDataException("PNG가 아닙니다");
                w = tex.width;
                h = tex.height;
                return tex.GetPixels32();
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }

        /// <summary>새로 불러온 게임 그림 중 번역할 것이 있으면 조각을 덮어씌운다.</summary>
        public void Scan()
        {
            if (_targets.Count == 0) return;
            foreach (Texture2D tex in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (tex == null) continue;
                Target t;
                if (!_targets.TryGetValue(tex.name, out t)) continue;
                int id = tex.GetInstanceID();
                if (_applied.ContainsKey(id) || _skipped.Contains(id)) continue;
                if (tex.width != t.Width || tex.height != t.Height)
                {
                    _skipped.Add(id);
                    continue;
                }
                try { Apply(tex, t, id); }
                catch (Exception e)
                {
                    _skipped.Add(id);
                    KoreanFontPlugin.Log.LogWarning("그림 한글화 실패 (" + tex.name + "): " + e.Message);
                }
            }
        }

        private void Apply(Texture2D tex, Target t, int id)
        {
            Color32[] original = ReadPixels(tex);
            var result = (Color32[])original.Clone();
            int done = 0;
            foreach (Patch p in t.Patches)
            {
                int bottom = t.Height - p.Y - p.H; // 유니티 좌표(아래쪽 기준)에서 조각의 첫 행
                if (!Matches(original, t.Width, p, bottom))
                {
                    if (_warned.Add(p.File)) KoreanFontPlugin.Log.LogWarning("게임 그림이 바뀌어 그림 조각을 건너뜁니다: " + p.File);
                    continue;
                }
                for (int row = 0; row < p.H; row++)
                    Array.Copy(p.Pixels, row * p.W, result, (bottom + row) * t.Width + p.X, p.W);
                done++;
            }
            if (done == 0)
            {
                _skipped.Add(id);
                return;
            }
            var a = new Applied { Texture = tex, Original = original, Translated = result };
            // 압축되었거나 읽기 전용인 그림은 직접 픽셀을 쓸 수 없으므로 PNG로 통째로 다시 불러온다 (그림 객체는 그대로라 스프라이트 연결이 유지된다)
            ImageConversion.LoadImage(tex, EncodePng(_translated ? result : original, t.Width, t.Height), false);
            _applied[id] = a;
            KoreanFontPlugin.Log.LogInfo("그림 한글화: " + tex.name + " (조각 " + done + "개)");
        }

        private static bool Matches(Color32[] px, int width, Patch p, int bottom)
        {
            long diff = 0;
            int n = 0;
            for (int row = 0; row < p.H; row++)
            {
                for (int col = 0; col < p.W; col++)
                {
                    Color32 a = px[(bottom + row) * width + p.X + col];
                    Color32 b = p.Check[row * p.W + col];
                    if (a.a < 8 && b.a < 8) continue; // 완전히 투명한 곳의 색은 의미가 없다
                    diff += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) + Math.Abs(a.a - b.a);
                    n++;
                }
            }
            return n == 0 || diff / (4.0 * n) <= 8.0;
        }

        /// <summary>읽기 전용이거나 압축된 그림도 화면에 그려서 픽셀을 읽는다.</summary>
        private static Color32[] ReadPixels(Texture2D tex)
        {
            if (tex.isReadable)
            {
                try { return tex.GetPixels32(); }
                catch (Exception) { }
            }
            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture prev = RenderTexture.active;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                copy.Apply();
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(copy);
            }
        }

        private static byte[] EncodePng(Color32[] px, int w, int h)
        {
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tmp.SetPixels32(px);
                tmp.Apply();
                return ImageConversion.EncodeToPNG(tmp);
            }
            finally { UnityEngine.Object.Destroy(tmp); }
        }

        /// <summary>한국어/영어 원문 전환 (XUnity의 ALT+T와 함께 움직인다)</summary>
        public void SetTranslated(bool on)
        {
            if (_translated == on) return;
            _translated = on;
            var gone = new List<int>();
            foreach (KeyValuePair<int, Applied> kv in _applied)
            {
                Applied a = kv.Value;
                if (a.Texture == null)
                {
                    gone.Add(kv.Key);
                    continue;
                }
                try
                {
                    a.Texture.SetPixels32(on ? a.Translated : a.Original);
                    a.Texture.Apply(a.Texture.mipmapCount > 1);
                }
                catch (Exception e)
                {
                    KoreanFontPlugin.Log.LogWarning("그림 전환 실패 (" + a.Texture.name + "): " + e.Message);
                }
            }
            foreach (int id in gone) _applied.Remove(id);
        }
    }
}

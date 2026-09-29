using System;
using System.IO;
using System.Windows.Forms;

namespace PPGKoreanInstaller
{
    internal static class Program
    {
        public static readonly string PatchVersion = "v" + typeof(Program).Assembly.GetName().Version.ToString(2);
        public const string RepoUrl = "https://github.com/O3O-2022/PeoplePlayground-Korean";
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "PPGKoreanInstaller.log");

        public static void FileLog(string s)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss ") + s + Environment.NewLine); } catch { }
        }

        /// <summary>
        /// 인자 없이 실행하면 GUI.
        /// 테스트/자동화용: --install &lt;폴더&gt; [--text-only] | --uninstall &lt;폴더&gt; (종료 코드 0 = 성공, 기록은 %TEMP%\PPGKoreanInstaller.log)
        /// --dir &lt;폴더&gt; : 해당 폴더로 GUI 시작 (관리자 권한 재실행에 사용)
        /// --screenshot &lt;폴더&gt; &lt;png&gt; [--as-steam-dir] : 창 모습을 이미지로 저장 후 종료 (--as-steam-dir: 그 폴더를 Steam 폴더로 취급해 경고 없이 찍는다)
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && (args[0] == "--install" || args[0] == "--uninstall"))
            {
                try
                {
                    var p = new Patcher(args[1], FileLog, null);
                    if (args[0] == "--install") p.Install(Array.IndexOf(args, "--text-only") < 0); else p.Uninstall();
                    FileLog("CLI OK " + args[0]);
                    return 0;
                }
                catch (Exception e)
                {
                    FileLog("CLI FAIL " + e);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string dir = args.Length >= 2 && (args[0] == "--dir" || args[0] == "--screenshot") ? args[1] : null;
            bool asSteam = args.Length >= 4 && args[0] == "--screenshot" && args[3] == "--as-steam-dir";
            var form = new MainForm(dir, asSteam ? dir : null);
            if (args.Length >= 3 && args[0] == "--screenshot")
            {
                form.Shown += (s, e) =>
                {
                    Application.DoEvents();
                    form.SaveScreenshot(args[2]);
                    form.Close();
                };
            }
            Application.Run(form);
            return 0;
        }
    }
}

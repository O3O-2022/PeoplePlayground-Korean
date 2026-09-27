using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PPGKoreanInstaller
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _path = new TextBox();
        private readonly Label _gameState = new Label();
        private readonly Label _patchState = new Label();
        private readonly Label _warn = new Label();
        private readonly Button _install = new Button();
        private readonly Button _uninstall = new Button();
        private readonly Button _run = new Button();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly TextBox _log = new TextBox();
        private readonly Color _ok = Color.FromArgb(30, 130, 60);
        private readonly Color _bad = Color.FromArgb(190, 50, 40);

        public MainForm(string initialDir)
        {
            Text = "People Playground 한국어 패치 설치 도우미 " + Program.PatchVersion;
            Font = new Font("Malgun Gothic", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 520);
            BackColor = Color.White;

            var title = new Label
            {
                Text = "People Playground 한국어 패치",
                Font = new Font("Malgun Gothic", 15f, FontStyle.Bold),
                AutoSize = true, Location = new Point(20, 16)
            };
            var sub = new Label
            {
                Text = "비공식 팬 번역 " + Program.PatchVersion + "  ·  게임 버전 1.27.18 기준",
                ForeColor = Color.DimGray, AutoSize = true, Location = new Point(22, 50)
            };

            // 1. 게임 폴더
            var g1 = new GroupBox { Text = "1. 게임 폴더", Location = new Point(20, 80), Size = new Size(580, 96) };
            _path.Location = new Point(14, 28); _path.Size = new Size(452, 26);
            _path.TextChanged += (s, e) => RefreshState();
            var browse = new Button { Text = "찾아보기...", Location = new Point(474, 26), Size = new Size(92, 30) };
            browse.Click += (s, e) => Browse();
            _gameState.Location = new Point(14, 62); _gameState.AutoSize = true;
            g1.Controls.AddRange(new Control[] { _path, browse, _gameState });

            // 2. 상태
            var g2 = new GroupBox { Text = "2. 상태", Location = new Point(20, 186), Size = new Size(580, 88) };
            _patchState.Location = new Point(14, 28); _patchState.AutoSize = true;
            _warn.Location = new Point(14, 54); _warn.Size = new Size(552, 30); _warn.ForeColor = _bad;
            g2.Controls.AddRange(new Control[] { _patchState, _warn });

            // 3. 설치 / 제거
            var g3 = new GroupBox { Text = "3. 설치", Location = new Point(20, 284), Size = new Size(580, 150) };
            _install.Text = "설치하기"; _install.Location = new Point(14, 28); _install.Size = new Size(180, 40);
            _install.Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
            _install.BackColor = Color.FromArgb(40, 110, 200); _install.ForeColor = Color.White; _install.FlatStyle = FlatStyle.Flat;
            _install.Click += async (s, e) => await Run(true);
            _uninstall.Text = "제거하기"; _uninstall.Location = new Point(204, 28); _uninstall.Size = new Size(120, 40);
            _uninstall.Click += async (s, e) => await Run(false);
            _run.Text = "게임 실행"; _run.Location = new Point(446, 28); _run.Size = new Size(120, 40);
            _run.Click += (s, e) => StartGame();
            _bar.Location = new Point(14, 80); _bar.Size = new Size(552, 16);
            _log.Location = new Point(14, 102); _log.Size = new Size(552, 40);
            _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical; _log.BackColor = Color.WhiteSmoke;
            g3.Controls.AddRange(new Control[] { _install, _uninstall, _run, _bar, _log });

            var link = new LinkLabel { Text = "GitHub 저장소 · 번역 오류 제보", AutoSize = true, Location = new Point(20, 448) };
            link.LinkClicked += (s, e) => Process.Start(Program.RepoUrl);
            var hint = new Label
            {
                Text = "게임 안에서 ALT+T: 한국어 ↔ 영어 전환   ALT+R: 번역 다시 불러오기",
                ForeColor = Color.DimGray, AutoSize = true, Location = new Point(20, 476)
            };
            Controls.AddRange(new Control[] { title, sub, g1, g2, g3, link, hint });

            if (Patcher.IsGameDir(initialDir)) _path.Text = initialDir;
            else
            {
                var found = Patcher.FindGameDirs();
                if (found.Count > 0) { _path.Text = found[0]; Log("Steam 라이브러리에서 게임 폴더를 찾았습니다."); }
                else Log("게임 폴더를 자동으로 찾지 못했습니다. [찾아보기]로 선택해 주세요.");
            }
            RefreshState();
            Shown += (s, e) => _path.Select(0, 0);
        }

        private void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            _log.AppendText((_log.TextLength > 0 ? Environment.NewLine : "") + s);
            Program.FileLog(s);
        }

        private void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = "People Playground 게임 폴더를 선택하세요 (People Playground.exe가 있는 폴더)" })
            {
                if (Directory.Exists(_path.Text)) d.SelectedPath = _path.Text;
                if (d.ShowDialog(this) == DialogResult.OK) _path.Text = d.SelectedPath;
            }
        }

        private void RefreshState()
        {
            string dir = _path.Text.Trim();
            bool game = Patcher.IsGameDir(dir);
            _gameState.Text = game ? "✔ People Playground를 찾았습니다." : "✖ 이 폴더에는 People Playground.exe가 없습니다.";
            _gameState.ForeColor = game ? _ok : _bad;
            _install.Enabled = game; _uninstall.Enabled = false; _run.Enabled = game;
            _warn.Text = "";
            if (!game) { _patchState.Text = "게임 폴더를 먼저 선택해 주세요."; _patchState.ForeColor = Color.DimGray; return; }

            var p = new Patcher(dir, null, null);
            string ver = p.InstalledVersion;
            if (ver != null)
            {
                _patchState.Text = "✔ 한국어 패치가 설치되어 있습니다 (" + ver + ")";
                _patchState.ForeColor = _ok;
                _install.Text = "다시 설치 / 업데이트";
                _uninstall.Enabled = true;
            }
            else
            {
                _patchState.Text = "아직 설치되지 않았습니다.";
                _patchState.ForeColor = Color.Black;
                _install.Text = "설치하기";
            }
            string foreign = p.ForeignLoader();
            if (foreign != null) _warn.Text = "⚠ " + foreign + "\n   설치하면 기존 로더는 PPGKorean_backup 폴더로 옮겨집니다.";
            if (p.GameRunning) _warn.Text = (_warn.Text.Length > 0 ? _warn.Text + "\n" : "") + "⚠ 게임이 실행 중입니다. 설치/제거 전에 게임을 꺼 주세요.";
        }

        private async Task Run(bool install)
        {
            string dir = _path.Text.Trim();
            var probe = new Patcher(dir, null, null);
            if (!probe.CanWrite())
            {
                if (MessageBox.Show(this, "이 폴더에 파일을 쓰려면 관리자 권한이 필요합니다.\n관리자 권한으로 다시 실행할까요?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--dir \"" + dir + "\"") { Verb = "runas", UseShellExecute = true });
                        Close();
                    }
                    catch (Exception e) { Log("관리자 권한 실행 취소: " + e.Message); }
                }
                return;
            }
            if (!install)
            {
                string msg = probe.HasManifest
                    ? "한국어 패치를 제거할까요?\n(설치 도우미가 설치한 파일만 지웁니다)"
                    : "설치 기록이 없어 이 패치가 넣는 파일(winhttp.dll, BepInEx의 한국어 파일 등)을 찾아 지웁니다.\n다른 BepInEx 플러그인은 지우지 않습니다. 계속할까요?";
                if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }

            SetBusy(true);
            _bar.Value = 0;
            var patcher = new Patcher(dir, Log, (i, n) => BeginInvoke(new Action(() => { _bar.Maximum = n; _bar.Value = Math.Min(i, n); })));
            try
            {
                await Task.Run(() => { if (install) patcher.Install(); else patcher.Uninstall(); });
                Log(install ? "✔ 설치가 끝났습니다. [게임 실행]을 눌러 확인해 보세요. (첫 실행은 조금 느립니다)" : "✔ 제거가 끝났습니다.");
                MessageBox.Show(this, install ? "설치가 끝났습니다!\n게임을 실행하면 한국어로 나옵니다." : "제거가 끝났습니다.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                Log("✖ 실패: " + e.Message);
                MessageBox.Show(this, e.Message, "실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                RefreshState();
            }
        }

        private void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            foreach (Control c in new Control[] { _install, _uninstall, _run, _path }) c.Enabled = !busy;
        }

        private void StartGame()
        {
            try { Process.Start("steam://rungameid/1118200"); Log("Steam으로 게임을 실행합니다."); }
            catch
            {
                string exe = Path.Combine(_path.Text.Trim(), Patcher.GameExe);
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _path.Text.Trim() });
            }
        }

        /// <summary>README용: 창을 그대로 이미지로 저장한다.</summary>
        public void SaveScreenshot(string file)
        {
            using (var bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}

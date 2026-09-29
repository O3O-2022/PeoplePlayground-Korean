using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PPGKoreanInstaller
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _path = new TextBox();
        private readonly Label _gameState = new Label();
        private readonly Label _steamWarn = new Label();
        private readonly LinkLabel _useSteamDir = new LinkLabel();
        private readonly RadioButton _optImage = new RadioButton();
        private readonly RadioButton _optText = new RadioButton();
        private readonly Label _patchState = new Label();
        private readonly Label _warn = new Label();
        private readonly Button _install = new Button();
        private readonly Button _uninstall = new Button();
        private readonly Button _run = new Button();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly TextBox _log = new TextBox();
        private readonly PictureBox _preview = new PictureBox();
        private readonly Label _previewCaption = new Label();
        private readonly Image _previewText = LoadImage("preview_text.png");
        private readonly Image _previewImage = LoadImage("preview_image.png");
        private readonly Color _ok = Color.FromArgb(30, 130, 60);
        private readonly Color _bad = Color.FromArgb(190, 50, 40);

        // Steam이 실제로 실행하는 게임 폴더 (못 찾으면 null)
        private readonly string _steamDir;
        // 사용자가 번역 범위를 직접 골랐으면 폴더를 바꿔도 그대로 둔다
        private bool _optTouched;
        private bool _settingOpt;

        /// <param name="steamDirOverride">README 캡처용: 이 폴더를 Steam이 실행하는 폴더로 취급한다</param>
        public MainForm(string initialDir, string steamDirOverride = null)
        {
            _steamDir = steamDirOverride ?? Patcher.SteamGameDir();
            Text = "People Playground 한국어 패치 설치 도우미 " + Program.PatchVersion;
            Font = new Font("Malgun Gothic", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1000, 654);
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
            var g1 = new GroupBox { Text = "1. 게임 폴더", Location = new Point(20, 80), Size = new Size(580, 138) };
            _path.Location = new Point(14, 28); _path.Size = new Size(452, 26);
            _path.TextChanged += (s, e) => RefreshState();
            var browse = new Button { Text = "찾아보기...", Location = new Point(474, 26), Size = new Size(92, 30) };
            browse.Click += (s, e) => Browse();
            _gameState.Location = new Point(14, 62); _gameState.AutoSize = true;
            _steamWarn.Location = new Point(14, 86); _steamWarn.Size = new Size(552, 22);
            _steamWarn.ForeColor = _bad;
            _steamWarn.Text = "⚠ Steam으로 켜는 게임은 이 폴더가 아니라 아래 폴더에 있습니다.";
            _useSteamDir.Location = new Point(14, 108); _useSteamDir.Size = new Size(552, 22); _useSteamDir.AutoEllipsis = true;
            _useSteamDir.LinkClicked += (s, e) => _path.Text = _steamDir;
            g1.Controls.AddRange(new Control[] { _path, browse, _gameState, _steamWarn, _useSteamDir });

            // 2. 번역 범위
            var g2 = new GroupBox { Text = "2. 번역 범위 (오른쪽에서 미리보기)", Location = new Point(20, 226), Size = new Size(580, 84) };
            _optImage.Text = "글자 + 그림 속 글자 (추천)  —  제목 로고, 표지판, 아이콘 속 글자까지";
            _optImage.Location = new Point(14, 24); _optImage.AutoSize = true;
            _optText.Text = "글자만  —  메뉴·설명·툴팁만 한글로, 그림은 원본 그대로";
            _optText.Location = new Point(14, 52); _optText.AutoSize = true;
            EventHandler optChanged = (s, e) =>
            {
                if (!_settingOpt) _optTouched = true;
                UpdatePreview();
            };
            _optImage.CheckedChanged += optChanged;
            _optText.CheckedChanged += optChanged;
            g2.Controls.AddRange(new Control[] { _optImage, _optText });

            // 3. 상태
            var g3 = new GroupBox { Text = "3. 상태", Location = new Point(20, 318), Size = new Size(580, 112) };
            _patchState.Location = new Point(14, 28); _patchState.AutoSize = true;
            _warn.Location = new Point(14, 52); _warn.Size = new Size(552, 56); _warn.ForeColor = _bad;
            g3.Controls.AddRange(new Control[] { _patchState, _warn });

            // 4. 설치 / 제거
            var g4 = new GroupBox { Text = "4. 설치", Location = new Point(20, 438), Size = new Size(580, 150) };
            _install.Text = "설치하기"; _install.Location = new Point(14, 28); _install.Size = new Size(210, 40);
            _install.Font = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
            _install.BackColor = Color.FromArgb(40, 110, 200); _install.ForeColor = Color.White; _install.FlatStyle = FlatStyle.Flat;
            _install.Click += async (s, e) => await Run(true);
            _uninstall.Text = "제거하기"; _uninstall.Location = new Point(234, 28); _uninstall.Size = new Size(120, 40);
            _uninstall.Click += async (s, e) => await Run(false);
            _run.Text = "게임 실행"; _run.Location = new Point(446, 28); _run.Size = new Size(120, 40);
            _run.Click += (s, e) => StartGame();
            _bar.Location = new Point(14, 80); _bar.Size = new Size(552, 16);
            _log.Location = new Point(14, 102); _log.Size = new Size(552, 40);
            _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical; _log.BackColor = Color.WhiteSmoke;
            g4.Controls.AddRange(new Control[] { _install, _uninstall, _run, _bar, _log });

            // 미리보기
            var gp = new GroupBox { Text = "미리보기", Location = new Point(620, 80), Size = new Size(360, 508) };
            _preview.Location = new Point(10, 24); _preview.Size = new Size(340, 400);
            _preview.SizeMode = PictureBoxSizeMode.Zoom; _preview.BackColor = Color.FromArgb(24, 24, 28);
            _previewCaption.Location = new Point(10, 430); _previewCaption.Size = new Size(340, 72);
            _previewCaption.ForeColor = Color.DimGray;
            gp.Controls.AddRange(new Control[] { _preview, _previewCaption });

            var link = new LinkLabel { Text = "GitHub 저장소 · 번역 오류 제보", AutoSize = true, Location = new Point(20, 600) };
            link.LinkClicked += (s, e) => Process.Start(Program.RepoUrl);
            var hint = new Label
            {
                Text = "게임 안에서 ALT+T: 한국어 ↔ 영어 전환   ALT+R: 번역 다시 불러오기",
                ForeColor = Color.DimGray, AutoSize = true, Location = new Point(20, 626)
            };
            Controls.AddRange(new Control[] { title, sub, g1, g2, g3, g4, gp, link, hint });

            SetOption(true);
            if (Patcher.IsGameDir(initialDir)) _path.Text = initialDir;
            else
            {
                var found = Patcher.FindGameDirs();
                if (found.Count > 0)
                {
                    _path.Text = found[0];
                    Log(Patcher.SameDir(found[0], _steamDir)
                        ? "Steam이 실행하는 게임 폴더를 찾았습니다."
                        : "Steam 라이브러리에서 게임 폴더를 찾았습니다.");
                }
                else Log("게임 폴더를 자동으로 찾지 못했습니다. [찾아보기]로 선택해 주세요.");
            }
            RefreshState();
            UpdatePreview();
            Shown += (s, e) => _path.Select(0, 0);
        }

        private static Image LoadImage(string name)
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                return s == null ? null : Image.FromStream(s);
            }
            catch { return null; }
        }

        private void SetOption(bool images)
        {
            _settingOpt = true;
            _optImage.Checked = images;
            _optText.Checked = !images;
            _settingOpt = false;
        }

        private void UpdatePreview()
        {
            bool images = _optImage.Checked;
            _preview.Image = images ? _previewImage : _previewText;
            _previewCaption.Text = images
                ? "메뉴·설명에 더해 그림 속 글자(제목 로고, 1톤 추, 밸브 표시판, 경고판, 아이콘)도 한글로 바뀝니다."
                : "메뉴·설명·툴팁만 한글로 바뀝니다. 그림 속 글자(제목 로고 등)는 영어 그대로입니다.";
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

        /// <summary>고른 폴더가 Steam이 실행하는 폴더와 다른지 (Steam 폴더를 모르면 false)</summary>
        private bool NotSteamDir(string dir)
        {
            return _steamDir != null && !Patcher.SameDir(dir, _steamDir);
        }

        private void RefreshState()
        {
            string dir = _path.Text.Trim();
            bool game = Patcher.IsGameDir(dir);
            _gameState.Text = game ? "✔ People Playground를 찾았습니다." : "✖ 이 폴더에는 People Playground.exe가 없습니다.";
            _gameState.ForeColor = game ? _ok : _bad;
            bool other = game && NotSteamDir(dir);
            _useSteamDir.Text = "▶ 이 폴더로 바꾸기: " + _steamDir;
            _steamWarn.Visible = other;
            _useSteamDir.Visible = other;
            _install.Enabled = game; _uninstall.Enabled = false; _run.Enabled = game;
            _warn.Text = "";
            if (!game) { _patchState.Text = "게임 폴더를 먼저 선택해 주세요."; _patchState.ForeColor = Color.DimGray; return; }

            var p = new Patcher(dir, null, null);
            string ver = p.InstalledVersion;
            if (ver != null)
            {
                _patchState.Text = "✔ 한국어 패치가 설치되어 있습니다 (" + ver + ", " + (p.HasImages ? "글자 + 그림" : "글자만") + ")";
                _patchState.ForeColor = _ok;
                _install.Text = "다시 설치 / 업데이트";
                _uninstall.Enabled = true;
                if (!_optTouched) SetOption(!p.TextOnlyChosen);
            }
            else
            {
                _patchState.Text = "아직 설치되지 않았습니다.";
                _patchState.ForeColor = Color.Black;
                _install.Text = "설치하기";
                if (!_optTouched) SetOption(true);
            }
            if (other) _warn.Text = "⚠ 이 폴더에 설치하면 Steam으로 게임을 켰을 때 한국어가 나오지 않습니다.";
            string foreign = p.ForeignLoader();
            if (foreign != null) _warn.Text = (_warn.Text.Length > 0 ? _warn.Text + "\n" : "") + "⚠ " + foreign + "\n   설치하면 이 로더는 PPGKorean_backup 폴더로 옮겨져 꺼집니다.";
            if (p.GameRunning) _warn.Text = (_warn.Text.Length > 0 ? _warn.Text + "\n" : "") + "⚠ 게임이 실행 중입니다. 설치/제거 전에 게임을 꺼 주세요.";
            UpdatePreview();
        }

        private async Task Run(bool install)
        {
            string dir = _path.Text.Trim();
            bool images = _optImage.Checked;
            var probe = new Patcher(dir, null, null);
            if (install && NotSteamDir(dir))
            {
                string msg = "고른 폴더는 Steam이 실행하는 게임 폴더가 아닙니다.\n\n" +
                             "고른 폴더: " + dir + "\nSteam 폴더: " + _steamDir + "\n\n" +
                             "여기에 설치하면 Steam으로 게임을 켰을 때 한국어가 나오지 않습니다.\n" +
                             "Steam 폴더에 설치할까요? (아니요: 고른 폴더에 그대로 설치)";
                DialogResult r = MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes)
                {
                    _path.Text = _steamDir;
                    dir = _steamDir;
                    probe = new Patcher(dir, null, null);
                }
            }
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
                await Task.Run(() => { if (install) patcher.Install(images); else patcher.Uninstall(); });
                Log(install ? "✔ 설치가 끝났습니다. [게임 실행]을 눌러 확인해 보세요. (첫 실행은 조금 느립니다)" : "✔ 제거가 끝났습니다.");
                string done = install ? "설치가 끝났습니다!\n게임을 실행하면 한국어로 나옵니다." : "제거가 끝났습니다.";
                if (install && NotSteamDir(dir))
                    done = "설치가 끝났습니다.\n\n단, Steam은 다른 폴더(" + _steamDir + ")의 게임을 실행하므로\nSteam으로 켜면 한국어가 나오지 않습니다. [게임 실행]은 이 폴더의 게임을 직접 켭니다.";
                MessageBox.Show(this, done, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            foreach (Control c in new Control[] { _install, _uninstall, _run, _path, _optImage, _optText }) c.Enabled = !busy;
        }

        private void StartGame()
        {
            string dir = _path.Text.Trim();
            // Steam은 자기 폴더의 게임을 켜므로, 다른 폴더를 골랐으면 그 폴더의 exe를 직접 켠다
            if (!NotSteamDir(dir))
            {
                try { Process.Start("steam://rungameid/" + Patcher.AppId); Log("Steam으로 게임을 실행합니다."); return; }
                catch { }
            }
            Process.Start(new ProcessStartInfo(Path.Combine(dir, Patcher.GameExe)) { WorkingDirectory = dir });
            Log("이 폴더의 게임을 직접 실행합니다.");
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

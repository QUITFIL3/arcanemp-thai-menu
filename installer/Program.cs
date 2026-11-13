using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("ArcaneMP Thai Menu Installer")]
[assembly: AssemblyDescription("Thai menu installer with automatic menu detection and backups")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]

namespace ArcaneThai
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] != "--render") {
                try {
                    object result;
                    switch (args[0]) {
                        case "--inspect": result = PatchEngine.Inspect(args[1]); break;
                        case "--install": result = PatchEngine.Install(args[1]); break;
                        case "--restore": result = PatchEngine.Restore(args[1]); break;
                        case "--extract":
                            if (args.Length < 3) throw new ArgumentException("Specify an output HTML file.");
                            File.WriteAllText(args[2], PatchEngine.ExtractForTests(PatchEngine.ResolveTarget(args[1])), new System.Text.UTF8Encoding(false));
                            return 0;
                        default: throw new ArgumentException("Unknown command.");
                    }
                    string json = PatchEngine.Serialize(result);
                    if (args.Length >= 3) File.WriteAllText(args[2], json, new System.Text.UTF8Encoding(false));
                    else Console.WriteLine(json);
                    return 0;
                } catch (Exception error) {
                    string json = PatchEngine.Serialize(new { Error = error.Message });
                    if (args.Length >= 3) File.WriteAllText(args[2], json, new System.Text.UTF8Encoding(false));
                    else Console.Error.WriteLine(json);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new InstallerForm()) {
                if (args.Length >= 2 && args[0] == "--render") {
                    form.RenderPreview(args[1]);
                    return 0;
                }
                Application.Run(form);
            }
            return 0;
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly TextBox location = new TextBox();
        private readonly Button browseFolder = new Button();
        private readonly Button browseFile = new Button();
        private readonly Button install = new Button();
        private readonly Button restore = new Button();
        private readonly Button openBackup = new Button();
        private readonly Label stateTitle = new Label();
        private readonly Label stateDetail = new Label();
        private readonly Panel statePanel = new Panel();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Timer debounce = new Timer();
        private Inspection inspection;
        private string backupPath;
        private int revision;
        private bool writing;
        private bool rendering;
        private static readonly Color Ink = Color.FromArgb(28, 38, 52);
        private static readonly Color Muted = Color.FromArgb(74, 88, 105);
        private static readonly Color Accent = Color.FromArgb(31, 83, 170);

        public InstallerForm()
        {
            Text = "ตัวติดตั้งภาษาไทย ArcaneMP 0.3.0";
            ClientSize = new Size(820, 640);
            MinimumSize = new Size(780, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            ForeColor = Ink;
            Font = new Font("Leelawadee UI", 10.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ShowIcon = false;

            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(30, 24, 30, 22),
                ColumnCount = 1, RowCount = 10
            };
            int[] heights = { 48, 66, 28, 44, 48, 120, 18, 62, 42 };
            foreach (int height in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            var title = new Label {
                Text = "ติดตั้งเมนูภาษาไทย", AutoSize = false, Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 21f, FontStyle.Bold), Margin = Padding.Empty
            };
            layout.Controls.Add(title, 0, 0);
            layout.Controls.Add(new Label {
                Text = "เลือกโฟลเดอร์ client หรือไฟล์ ArcaneMP\nติดตั้งภาษาไทยแยกไฟล์ โดยเก็บ DLL ทางการไว้สำหรับตัวอัปเดต",
                Dock = DockStyle.Fill, ForeColor = Muted, Margin = new Padding(0, 4, 0, 0)
            }, 0, 1);
            layout.Controls.Add(new Label {
                Text = "ตำแหน่งโปรแกรม", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty
            }, 0, 2);
            location.Dock = DockStyle.Fill;
            location.AutoSize = false;
            location.Margin = new Padding(0, 2, 0, 6);
            location.AccessibleName = "ตำแหน่งไคลเอนต์ ArcaneMP";
            location.Font = new Font("Segoe UI", 10.5f);
            layout.Controls.Add(location, 0, 3);

            var browse = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            ConfigureButton(browseFolder, "เลือกโฟลเดอร์...", 160, false);
            ConfigureButton(browseFile, "เลือกไฟล์...", 142, false);
            browse.Controls.Add(browseFolder);
            browse.Controls.Add(browseFile);
            layout.Controls.Add(browse, 0, 4);

            statePanel.Dock = DockStyle.Fill;
            statePanel.Padding = new Padding(16, 12, 16, 10);
            statePanel.Margin = new Padding(0, 8, 0, 8);
            statePanel.BackColor = Color.FromArgb(242, 245, 249);
            stateTitle.Dock = DockStyle.Fill;
            stateTitle.Font = new Font(Font, FontStyle.Bold);
            stateDetail.Dock = DockStyle.Fill;
            stateDetail.Padding = new Padding(0, 2, 0, 0);
            stateDetail.ForeColor = Muted;
            stateDetail.AutoEllipsis = true;
            var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusLayout.Controls.Add(stateTitle, 0, 0);
            statusLayout.Controls.Add(stateDetail, 0, 1);
            statePanel.Controls.Add(statusLayout);
            layout.Controls.Add(statePanel, 0, 5);

            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0, 3, 0, 3);
            progress.Style = ProgressBarStyle.Marquee;
            progress.MarqueeAnimationSpeed = 25;
            progress.Visible = false;
            layout.Controls.Add(progress, 0, 6);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            ConfigureButton(install, "ติดตั้งภาษาไทย", 198, true);
            ConfigureButton(restore, "คืนค่าเดิม", 138, false);
            ConfigureButton(openBackup, "เปิดโฟลเดอร์สำรอง", 192, false);
            install.Enabled = restore.Enabled = false;
            openBackup.Visible = false;
            actions.Controls.Add(install);
            actions.Controls.Add(restore);
            actions.Controls.Add(openBackup);
            layout.Controls.Add(actions, 0, 7);
            layout.Controls.Add(new Label {
                Text = "ปิด Hogwarts Legacy และ ArcaneMP ก่อนติดตั้งหรือคืนค่า",
                ForeColor = Muted, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0)
            }, 0, 8);
            var license = new LinkLabel { Text = "ใบอนุญาตฟอนต์ Noto Sans Thai", Dock = DockStyle.Fill, AutoSize = true, LinkColor = Muted, Margin = Padding.Empty };
            license.LinkClicked += (sender, e) => {
                using (var dialog = new Form { Text = "ใบอนุญาตฟอนต์", Size = new Size(720, 540), StartPosition = FormStartPosition.CenterParent, ShowIcon = false }) {
                    dialog.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = PatchEngine.FontLicenseText, Font = new Font("Segoe UI", 10f), BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = false });
                    dialog.ShowDialog(this);
                }
            };
            layout.Controls.Add(license, 0, 9);

            SetStatus("ยังไม่ได้เลือกไฟล์", "ตำแหน่งที่เลือกต้องมีไฟล์ ArcaneMPClient.dll", false, false);
            debounce.Interval = 450;
            debounce.Tick += async (sender, e) => { debounce.Stop(); await InspectSelected(); };
            location.TextChanged += (sender, e) => {
                revision++; inspection = null; install.Enabled = restore.Enabled = false;
                if (!writing && !rendering) { debounce.Stop(); debounce.Start(); }
            };
            location.KeyDown += async (sender, e) => {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; debounce.Stop(); await InspectSelected(); }
            };
            browseFolder.Click += (sender, e) => ChooseFolder();
            browseFile.Click += (sender, e) => ChooseFile();
            install.Click += async (sender, e) => await Change(false);
            restore.Click += async (sender, e) => await Change(true);
            openBackup.Click += (sender, e) => {
                if (!String.IsNullOrEmpty(backupPath) && Directory.Exists(Path.GetDirectoryName(backupPath)))
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(backupPath) + "\"") { UseShellExecute = true });
            };
            FormClosing += (sender, e) => {
                if (writing) { e.Cancel = true; SetStatus("กำลังบันทึกไฟล์", "โปรดรอให้การติดตั้งหรือคืนค่าเสร็จก่อนปิดหน้าต่าง", false, false); }
            };
        }

        private void ConfigureButton(Button button, string text, int width, bool primary)
        {
            button.Text = text;
            button.Size = new Size(width, 40);
            button.Margin = new Padding(0, 5, 10, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(190, 199, 212);
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = primary ? Accent : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.AccessibleName = text;
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "เลือกโฟลเดอร์ client ที่มี ArcaneMPClient.dll", ShowNewFolderButton = false }) {
                if (Directory.Exists(location.Text)) dialog.SelectedPath = location.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) location.Text = dialog.SelectedPath;
            }
        }

        private void ChooseFile()
        {
            using (var dialog = new OpenFileDialog {
                Title = "เลือกไฟล์ ArcaneMP", Filter = "ไฟล์ ArcaneMP|ArcaneMPClient.dll;ArcaneMPLauncher.exe|ไฟล์ DLL|*.dll",
                CheckFileExists = true, Multiselect = false, RestoreDirectory = true
            }) {
                if (dialog.ShowDialog(this) == DialogResult.OK) location.Text = dialog.FileName;
            }
        }

        private async Task InspectSelected()
        {
            if (writing || IsDisposed || String.IsNullOrWhiteSpace(location.Text)) return;
            int selectedRevision = revision;
            string selected = location.Text;
            progress.Visible = true;
            SetStatus("กำลังตรวจไฟล์...", "กำลังค้นหาหน้าเมนูของไฟล์ที่เลือก", false, false);
            try {
                Inspection result = await Task.Run(() => PatchEngine.Inspect(selected));
                if (IsDisposed || selectedRevision != revision) return;
                inspection = result;
                install.Enabled = !result.IsInstalled;
                restore.Enabled = result.CanRestore;
                install.Text = result.IsInstalled ? "ติดตั้งแล้ว" : "ติดตั้งภาษาไทย";
                SetStatus(result.IsInstalled ? "ติดตั้งภาษาไทยชุดนี้แล้ว" : result.NeedsRefresh ? "พร้อมปรับภาษาไทยให้ตรงกับ client" : "พร้อมติดตั้งภาษาไทย",
                    result.IsInstalled ? "เปิด ArcaneMP ใหม่ได้ หลังอัปเดต client ให้ติดตั้งภาษาไทยซ้ำ" : "ใช้ DLL ภาษาไทยแยกไฟล์ พร้อมสำรองการตั้งค่า Launcher", false, result.IsInstalled);
            } catch (Exception error) {
                if (IsDisposed || selectedRevision != revision) return;
                inspection = null;
                SetStatus("ยังติดตั้งไม่ได้", error.Message, true, false);
            } finally {
                if (!IsDisposed && selectedRevision == revision) progress.Visible = false;
            }
        }

        private async Task Change(bool restoring)
        {
            if (writing || inspection == null) return;
            writing = true;
            debounce.Stop();
            location.Enabled = browseFolder.Enabled = browseFile.Enabled = false;
            install.Enabled = restore.Enabled = false;
            progress.Visible = true;
            SetStatus(restoring ? "กำลังคืนค่าเดิม..." : "กำลังติดตั้งภาษาไทย...", "กำลังสำรองและตรวจสอบไฟล์", false, false);
            string target = inspection.TargetPath;
            try {
                OperationResult result = await Task.Run(() => restoring ? PatchEngine.Restore(target) : PatchEngine.Install(target));
                backupPath = result.BackupPath;
                openBackup.Visible = !String.IsNullOrEmpty(backupPath);
                inspection = await Task.Run(() => PatchEngine.Inspect(target));
                install.Enabled = !inspection.IsInstalled;
                restore.Enabled = inspection.CanRestore;
                install.Text = inspection.IsInstalled ? "ติดตั้งแล้ว" : "ติดตั้งภาษาไทย";
                SetStatus(restoring ? "คืนค่าเรียบร้อย" : "ติดตั้งเรียบร้อย", result.Message, false, true);
            } catch (Exception error) {
                install.Enabled = inspection != null && !inspection.IsInstalled;
                restore.Enabled = inspection != null && inspection.CanRestore;
                SetStatus("ดำเนินการไม่สำเร็จ", error.Message, true, false);
            } finally {
                writing = false;
                location.Enabled = browseFolder.Enabled = browseFile.Enabled = true;
                progress.Visible = false;
            }
        }

        private void SetStatus(string title, string detail, bool error, bool success)
        {
            stateTitle.Text = title;
            stateDetail.Text = detail;
            statePanel.BackColor = error ? Color.FromArgb(255, 239, 237) : success ? Color.FromArgb(232, 246, 238) : Color.FromArgb(240, 245, 253);
            stateTitle.ForeColor = error ? Color.FromArgb(147, 40, 32) : success ? Color.FromArgb(25, 103, 66) : Ink;
        }

        public void RenderPreview(string output)
        {
            rendering = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            location.Text = @"F:\ArcaneMP\ArcaneMPClient.dll";
            install.Enabled = true;
            SetStatus("พร้อมติดตั้งภาษาไทย", "ใช้ DLL ภาษาไทยแยกไฟล์ พร้อมสำรองการตั้งค่า Launcher", false, false);
            Show();
            Application.DoEvents();
            PerformLayout();
            using (var bitmap = new Bitmap(Width, Height)) {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
            Hide();
        }

        protected override bool ShowWithoutActivation { get { return rendering; } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) debounce.Dispose();
            base.Dispose(disposing);
        }
    }
}

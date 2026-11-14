using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArcaneThai
{
    internal sealed class GameThaiPanel : UserControl
    {
        private readonly ComboBox mode = new ComboBox();
        private readonly TextBox location = new TextBox();
        private readonly Button folder = new Button();
        private readonly Button file = new Button();
        private readonly Button scan = new Button();
        private readonly Button install = new Button();
        private readonly Button uninstall = new Button();
        private readonly Button backup = new Button();
        private readonly Panel status = new Panel();
        private readonly Label heading = new Label();
        private readonly Label detail = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Timer debounce = new Timer();
        private GameThaiInspection inspection;
        private string backupPath;
        private string clientPath = "";
        private string gamePath = "";
        private bool rendering;
        private bool switching;
        private int revision;
        public bool IsBusy { get; private set; }
        private string Mode { get { return mode.SelectedIndex == 1 ? "game" : "arcanemp"; } }
        private static readonly Color Ink = Color.FromArgb(28, 38, 52);
        private static readonly Color Muted = Color.FromArgb(74, 88, 105);
        private static readonly Color Accent = Color.FromArgb(31, 83, 170);

        public GameThaiPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            ForeColor = Ink;
            Font = new Font("Leelawadee UI", 10.5f);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 13, Padding = new Padding(30, 18, 30, 18), AutoScroll = true };
            int[] heights = { 42, 42, 44, 42, 26, 40, 46, 94, 16, 56, 44, 26 };
            foreach (int height in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "ติดตั้งภาษาไทยตัวเกม", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 21f, FontStyle.Bold), Margin = Padding.Empty }, 0, 0);
            layout.Controls.Add(Label("เมนู ไอเทม เควสต์ และคำบรรยายจากคำแปลชุมชน\nชุดนี้ยังมีข้อความอังกฤษบางส่วน"), 0, 1);
            layout.Controls.Add(Label("มีข้อความไทย 40,520 จาก 56,112 รายการ (ประมาณ 72%)\nเสียงพากย์และป้ายรูปภาพยังเป็นภาษาเดิม"), 0, 2);

            var modeRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            modeRow.Controls.Add(new Label { Text = "ติดตั้งสำหรับ", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
            mode.DropDownStyle = ComboBoxStyle.DropDownList;
            mode.Dock = DockStyle.Fill;
            mode.Margin = new Padding(0, 5, 0, 4);
            mode.Items.AddRange(new object[] { "เล่นผ่าน ArcaneMP", "ตัวเกมปกติ (Steam / Epic)" });
            mode.SelectedIndex = 0;
            mode.AccessibleName = "รูปแบบการติดตั้งภาษาไทยตัวเกม";
            modeRow.Controls.Add(mode, 1, 0);
            layout.Controls.Add(modeRow, 0, 3);
            layout.Controls.Add(new Label { Text = "ตำแหน่งโฟลเดอร์", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty }, 0, 4);
            location.Dock = DockStyle.Fill;
            location.Font = new Font("Segoe UI", 10.5f);
            location.AccessibleName = "โฟลเดอร์ ArcaneMP หรือ Hogwarts Legacy";
            location.Margin = new Padding(0, 2, 0, 4);
            layout.Controls.Add(location, 0, 5);
            var browse = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            Button(folder, "เลือกโฟลเดอร์...", 160, false);
            Button(file, "เลือกไฟล์...", 142, false);
            Button(scan, "ตรวจใหม่", 110, false);
            browse.Controls.AddRange(new Control[] { folder, file, scan });
            layout.Controls.Add(browse, 0, 6);

            status.Dock = DockStyle.Fill;
            status.Padding = new Padding(16, 8, 16, 8);
            status.Margin = new Padding(0, 4, 0, 4);
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.Dock = detail.Dock = DockStyle.Fill;
            heading.Font = new Font(Font, FontStyle.Bold);
            detail.ForeColor = Muted;
            detail.AutoEllipsis = true;
            rows.Controls.Add(heading, 0, 0);
            rows.Controls.Add(detail, 0, 1);
            status.Controls.Add(rows);
            layout.Controls.Add(status, 0, 7);
            progress.Dock = DockStyle.Fill;
            progress.Style = ProgressBarStyle.Marquee;
            progress.Visible = false;
            layout.Controls.Add(progress, 0, 8);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            Button(install, "ติดตั้งภาษาไทยตัวเกม", 224, true);
            Button(uninstall, "ถอนแพ็กตัวเกม", 154, false);
            Button(backup, "เปิดโฟลเดอร์สำรอง", 192, false);
            install.Enabled = uninstall.Enabled = false;
            backup.Visible = false;
            actions.Controls.AddRange(new Control[] { install, uninstall, backup });
            layout.Controls.Add(actions, 0, 9);
            layout.Controls.Add(Label("ใช้ภาษา English ของตัวเกม แล้วเปิดเกมใหม่\nปิด Hogwarts Legacy และ ArcaneMP ก่อนติดตั้งหรือถอนแพ็ก"), 0, 10);
            var credits = new LinkLabel { Text = "เครดิตคำแปลและฟอนต์ตัวเกม", Dock = DockStyle.Fill, LinkColor = Muted, Margin = Padding.Empty };
            credits.LinkClicked += (sender, e) => {
                using (var dialog = new Form { Text = "เครดิตคำแปลตัวเกม", Size = new Size(760, 560), StartPosition = FormStartPosition.CenterParent, ShowIcon = false }) {
                    dialog.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = GameThaiEngine.Credits, Font = new Font("Segoe UI", 10f), BackColor = Color.White, BorderStyle = BorderStyle.None });
                    dialog.ShowDialog(this);
                }
            };
            layout.Controls.Add(credits, 0, 11);
            SetStatus("ยังไม่ได้เลือกโฟลเดอร์", "เลือกโฟลเดอร์ client ของ ArcaneMP เพื่อแปลตัวเกมที่เปิดผ่าน client นั้น", false, false);
            debounce.Interval = 450;
            debounce.Tick += async (sender, e) => { debounce.Stop(); await InspectSelected(); };
            location.TextChanged += (sender, e) => {
                revision++; inspection = null; install.Enabled = uninstall.Enabled = false;
                if (!IsBusy && !rendering && !switching) { debounce.Stop(); debounce.Start(); }
            };
            location.KeyDown += async (sender, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; debounce.Stop(); await InspectSelected(); } };
            mode.SelectedIndexChanged += async (sender, e) => {
                switching = true;
                if (mode.SelectedIndex == 1) { clientPath = location.Text; location.Text = gamePath; }
                else { gamePath = location.Text; location.Text = clientPath; }
                switching = false;
                revision++; inspection = null; install.Enabled = uninstall.Enabled = false;
                debounce.Stop();
                SetStatus("เลือกตำแหน่งติดตั้ง", Mode == "game" ? "เลือกโฟลเดอร์ Hogwarts Legacy ที่มีโฟลเดอร์ Phoenix" : "เลือกโฟลเดอร์ที่มี ArcaneMPClient.dll", false, false);
                if (!rendering) await InspectSelected();
            };
            folder.Click += (sender, e) => {
                using (var picker = new FolderBrowserDialog { Description = Mode == "game" ? "เลือกโฟลเดอร์ Hogwarts Legacy" : "เลือกโฟลเดอร์ client ของ ArcaneMP", ShowNewFolderButton = false }) {
                    if (Directory.Exists(location.Text)) picker.SelectedPath = location.Text;
                    if (picker.ShowDialog(this) == DialogResult.OK) location.Text = picker.SelectedPath;
                }
            };
            file.Click += (sender, e) => {
                using (var picker = new OpenFileDialog { Title = "เลือกไฟล์ของโปรแกรม", Filter = Mode == "game" ? "ตัวเกม Hogwarts Legacy|HogwartsLegacy.exe" : "ไฟล์ ArcaneMP|ArcaneMPClient.dll;ArcaneMPLauncher.exe", CheckFileExists = true, RestoreDirectory = true }) {
                    if (picker.ShowDialog(this) == DialogResult.OK) location.Text = picker.FileName;
                }
            };
            scan.Click += async (sender, e) => { debounce.Stop(); await InspectSelected(); };
            install.Click += async (sender, e) => await Change(false);
            uninstall.Click += async (sender, e) => await Change(true);
            backup.Click += (sender, e) => { if (!String.IsNullOrEmpty(backupPath)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(backupPath) + "\"") { UseShellExecute = true }); };
        }

        private Label Label(string text) { return new Label { Text = text, Dock = DockStyle.Fill, ForeColor = Muted, Margin = Padding.Empty }; }
        private void Button(Button button, string text, int width, bool primary)
        {
            button.Text = text; button.Size = new Size(width, 38); button.Margin = new Padding(0, 4, 10, 0);
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(190, 199, 212);
            button.BackColor = primary ? Accent : Color.White; button.ForeColor = primary ? Color.White : Ink;
            button.UseVisualStyleBackColor = false; button.Cursor = Cursors.Hand;
        }

        private async Task InspectSelected()
        {
            if (IsBusy || rendering || IsDisposed) return;
            if (String.IsNullOrWhiteSpace(location.Text)) { SetStatus("ยังไม่ได้เลือกโฟลเดอร์", "เลือกตำแหน่งที่ต้องการติดตั้งภาษาไทยตัวเกม", false, false); return; }
            int selectedRevision = revision;
            string path = location.Text, selectedMode = Mode;
            progress.Visible = true;
            try {
                GameThaiInspection result = await Task.Run(() => GameThaiEngine.Inspect(path, selectedMode));
                if (IsDisposed || selectedRevision != revision) return;
                inspection = result;
                UpdateButtons();
                SetStatus(result.HasConflict ? "มีแพ็กอื่นใช้ชื่อเดียวกัน" : result.IsInstalled ? "ติดตั้งแพ็กตัวเกมไว้แล้ว" : result.NeedsMigration ? "พร้อมย้ายแพ็กเข้าโฟลเดอร์ mods" : "พร้อมติดตั้งภาษาไทยตัวเกม", result.Message, result.HasConflict, result.IsInstalled);
            } catch (Exception error) {
                if (IsDisposed || selectedRevision != revision) return;
                inspection = null; install.Enabled = uninstall.Enabled = false;
                SetStatus("ยังติดตั้งไม่ได้", error.Message, true, false);
            } finally { if (!IsDisposed && selectedRevision == revision) progress.Visible = false; }
        }

        private void UpdateButtons()
        {
            install.Enabled = inspection != null && !inspection.IsInstalled && !inspection.HasConflict;
            uninstall.Enabled = inspection != null && inspection.CanUninstall;
            install.Text = inspection != null && inspection.NeedsMigration ? "ย้ายแพ็กเข้า mods" : "ติดตั้งภาษาไทยตัวเกม";
        }

        private async Task Change(bool removing)
        {
            if (IsBusy || inspection == null) return;
            IsBusy = true; debounce.Stop();
            string path = inspection.RootPath, selectedMode = inspection.Mode;
            mode.Enabled = location.Enabled = folder.Enabled = file.Enabled = scan.Enabled = install.Enabled = uninstall.Enabled = false;
            progress.Visible = true;
            SetStatus(removing ? "กำลังถอนแพ็กตัวเกม..." : "กำลังติดตั้งแพ็กตัวเกม...", "กำลังตรวจสอบและบันทึกแพ็กภาษาไทย", false, false);
            try {
                GameThaiResult result = await Task.Run(() => removing ? GameThaiEngine.Uninstall(path, selectedMode) : GameThaiEngine.Install(path, selectedMode));
                backupPath = result.BackupPath; backup.Visible = !String.IsNullOrEmpty(backupPath);
                inspection = await Task.Run(() => GameThaiEngine.Inspect(path, selectedMode));
                SetStatus(removing ? "ถอนแพ็กเรียบร้อย" : "ติดตั้งตัวเกมภาษาไทยแล้ว", result.Message, false, true);
            } catch (Exception error) {
                SetStatus("ดำเนินการไม่สำเร็จ", error is UnauthorizedAccessException ? "ไม่มีสิทธิ์เขียนโฟลเดอร์นี้ โปรดเลือกตำแหน่งที่มีสิทธิ์ หรือเปิดตัวติดตั้งด้วยสิทธิ์ผู้ดูแลระบบ" : error.Message, true, false);
            } finally {
                IsBusy = false; progress.Visible = false;
                mode.Enabled = location.Enabled = folder.Enabled = file.Enabled = scan.Enabled = true;
                UpdateButtons();
            }
        }

        private void SetStatus(string title, string text, bool error, bool success)
        {
            heading.Text = title; detail.Text = text;
            status.BackColor = error ? Color.FromArgb(255, 239, 237) : success ? Color.FromArgb(232, 246, 238) : Color.FromArgb(240, 245, 253);
            heading.ForeColor = error ? Color.FromArgb(147, 40, 32) : success ? Color.FromArgb(25, 103, 66) : Ink;
        }

        public void SetDefaultClient(string path)
        {
            if (Mode == "arcanemp" && String.IsNullOrWhiteSpace(location.Text) && !String.IsNullOrWhiteSpace(path)) location.Text = path;
        }

        public void RenderPreview(bool direct)
        {
            rendering = true; debounce.Stop();
            mode.SelectedIndex = direct ? 1 : 0;
            location.Text = direct ? @"D:\Epic Games\Hogwarts Legacy" : @"F:\ArcaneMP";
            install.Enabled = true;
            SetStatus("พร้อมติดตั้งภาษาไทยตัวเกม", direct ? "ติดตั้งแพ็กเสริมลงโฟลเดอร์เกมที่เลือก สำหรับเปิดเกมปกติด้วย" : "เก็บแพ็กใน mods\\ArcaneThai และเชื่อมให้ ArcaneMP โหลดไฟล์เดียวกัน", false, false);
        }

        protected override void Dispose(bool disposing) { if (disposing) debounce.Dispose(); base.Dispose(disposing); }
    }
}

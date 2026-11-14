using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ArcaneThai
{
    internal sealed class InstallSnapshot
    {
        public string Root;
        public string GameRoot;
        public Inspection Menu;
        public GameThaiInspection Game;
        public string MenuError;
        public string GameError;
    }

    internal sealed class InstallPage : UserControl
    {
        private readonly bool direct;
        internal readonly TextBox FolderInput = new TextBox();
        internal readonly ComponentRow MenuRow = new ComponentRow("เมนู ArcaneMP", "หน้าเล่น การตั้งค่า และไปรษณีย์นกฮูก");
        internal readonly ComponentRow GameRow = new ComponentRow("ข้อความตัวเกม", "เมนู ไอเทม เควสต์ และคำบรรยายบางส่วน");
        internal readonly ActionButton Install = new ActionButton { Text = "ติดตั้งส่วนที่เลือก", Primary = true };
        internal readonly ActionButton Remove = new ActionButton { Text = "คืนค่า / ถอนแพ็ก" };
        private readonly ActionButton browse = new ActionButton { Text = "เลือกโฟลเดอร์" };
        private readonly ActionButton scan = new ActionButton { Text = "ตรวจใหม่" };
        private readonly ActionButton openBackup = new ActionButton { Text = "เปิดไฟล์สำรอง", Visible = false };
        private readonly Label statusTitle = Ui.Label("เลือกโฟลเดอร์เพื่อเริ่มต้น", 11, Ui.Gold, true);
        private readonly Label statusDetail = Ui.Label("โปรแกรมจะตรวจเมนูและแพ็กตัวเกมให้จากโฟลเดอร์เดียวกัน", 10, Ui.Muted);
        private readonly Label selected = Ui.Label("เลือก 2 ส่วน", 9.5f, Ui.Muted);
        private readonly Label processNotice = Ui.Label("ปิดเกมและ ArcaneMP Launcher ก่อนติดตั้งหรือถอนแพ็ก", 9.5f, Ui.Muted);
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Timer debounce = new Timer { Interval = 450 };
        private readonly Timer processTimer = new Timer { Interval = 2000 };
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000 };
        private InstallSnapshot snapshot;
        private string backupPath;
        private int revision;
        private bool preview;
        private bool inspecting;
        private bool targetRunning;
        public bool IsBusy { get; private set; }
        public string CurrentRoot { get { return snapshot == null ? "" : snapshot.Root; } }

        public InstallPage(bool directGame)
        {
            direct = directGame;
            Dock = DockStyle.Fill; BackColor = Ui.Canvas; ForeColor = Ui.Text; Font = Ui.Font();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12, Padding = new Padding(28, 22, 28, 18) };
            int[] heights = { 44, 30, 24, 44, 36, 24, direct ? 74 : 150, 12, 72, 8, 46 };
            foreach (int value in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, value));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            layout.Controls.Add(Ui.Label(direct ? "ภาษาไทยสำหรับตัวเกม" : "ติดตั้งภาษาไทย", 22, Ui.Text, true), 0, 0);
            layout.Controls.Add(Ui.Label(direct ? "ติดตั้งแพ็กข้อความลงเกม Steam หรือ Epic โดยตรง" : "เลือกโฟลเดอร์ครั้งเดียว แล้วเลือกส่วนที่ต้องการแปล", 10.5f, Ui.Muted), 0, 1);
            layout.Controls.Add(Ui.Label(direct ? "โฟลเดอร์ Hogwarts Legacy" : "โฟลเดอร์ ArcaneMP", 10.5f, Ui.Text, true), 0, 2);
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            pathRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
            var input = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Rail, Padding = new Padding(12, 9, 12, 7), Margin = new Padding(0, 0, 10, 0) };
            input.Paint += (sender, e) => { using (var pen = new Pen(FolderInput.Focused ? Ui.Gold : Ui.Line)) e.Graphics.DrawRectangle(pen, 0, 0, input.Width - 1, input.Height - 1); };
            FolderInput.BorderStyle = BorderStyle.None; FolderInput.Font = Ui.Font(); FolderInput.BackColor = Ui.Rail; FolderInput.ForeColor = Ui.Text; FolderInput.Dock = DockStyle.Fill;
            FolderInput.AccessibleName = direct ? "โฟลเดอร์ Hogwarts Legacy" : "โฟลเดอร์ ArcaneMP";
            FolderInput.GotFocus += (sender, e) => input.Invalidate(); FolderInput.LostFocus += (sender, e) => input.Invalidate();
            input.Controls.Add(FolderInput); browse.Dock = DockStyle.Fill; browse.Margin = Padding.Empty;
            pathRow.Controls.Add(input, 0, 0); pathRow.Controls.Add(browse, 1, 0); layout.Controls.Add(pathRow, 0, 3);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 2) };
            scan.Size = new Size(92, 30); scan.Margin = new Padding(0, 0, 10, 0);
            var chooseFile = new ActionButton { Text = "เลือกไฟล์แทน", Size = new Size(128, 30), Margin = Padding.Empty };
            tools.Controls.Add(scan); tools.Controls.Add(chooseFile); layout.Controls.Add(tools, 0, 4);
            layout.Controls.Add(Ui.Label("ส่วนที่ต้องการติดตั้ง", 10.5f, Ui.Text, true), 0, 5);
            var components = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = direct ? 1 : 3, Margin = Padding.Empty };
            if (!direct) { components.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); components.RowStyles.Add(new RowStyle(SizeType.Absolute, 1)); components.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); components.Controls.Add(MenuRow, 0, 0); components.Controls.Add(Ui.Rule(), 0, 1); components.Controls.Add(GameRow, 0, 2); }
            else { components.RowStyles.Add(new RowStyle(SizeType.Absolute, 74)); components.Controls.Add(GameRow, 0, 0); MenuRow.Choice.Checked = false; GameRow.Choice.Enabled = false; }
            layout.Controls.Add(components, 0, 6);
            var state = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 2, 0, 0) };
            state.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); state.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusDetail.AutoEllipsis = true; statusDetail.TextAlign = ContentAlignment.TopLeft;
            state.Controls.Add(statusTitle, 0, 0); state.Controls.Add(statusDetail, 0, 1); layout.Controls.Add(state, 0, 8);
            progress.Dock = DockStyle.Fill; progress.Style = ProgressBarStyle.Marquee; progress.Margin = new Padding(0, 3, 0, 4); progress.Visible = false; layout.Controls.Add(progress, 0, 9);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Install.Dock = Remove.Dock = openBackup.Dock = DockStyle.Fill;
            Install.Margin = new Padding(0, 0, 10, 5); Remove.Margin = new Padding(0, 0, 10, 5); openBackup.Margin = new Padding(0, 0, 10, 5);
            selected.TextAlign = ContentAlignment.MiddleRight;
            actions.Controls.Add(Install, 0, 0); actions.Controls.Add(Remove, 1, 0); actions.Controls.Add(openBackup, 2, 0); actions.Controls.Add(selected, 3, 0);
            layout.Controls.Add(actions, 0, 10); layout.Controls.Add(processNotice, 0, 11);
            processNotice.TextAlign = ContentAlignment.TopLeft; processNotice.Padding = new Padding(0, 6, 0, 0);
            tips.SetToolTip(GameRow, "คำแปลตัวเกมประมาณ 72% ใช้ภาษา English ของเกม เสียงพากย์ยังเป็นภาษาเดิม");
            tips.SetToolTip(Remove, "คืนค่าหรือถอนเฉพาะรายการที่เลือก พร้อมเก็บไฟล์สำรอง");
            Install.Enabled = Remove.Enabled = false;
            if (direct) { Install.Text = "ติดตั้งแพ็กตัวเกม"; Remove.Text = "ถอนแพ็กตัวเกม"; statusDetail.Text = "เลือกโฟลเดอร์เกมที่มี Phoenix แล้วใช้ภาษา English ของเกม"; }
            MenuRow.Choice.CheckedChanged += (sender, e) => SelectionChanged(); GameRow.Choice.CheckedChanged += (sender, e) => SelectionChanged();
            FolderInput.TextChanged += (sender, e) => { revision++; snapshot = null; backupPath = null; openBackup.Visible = false; ResetRows(); UpdateActions(); if (!preview && !IsBusy) { debounce.Stop(); debounce.Start(); } };
            debounce.Tick += async (sender, e) => { debounce.Stop(); await InspectSelected(); };
            scan.Click += async (sender, e) => { debounce.Stop(); await InspectSelected(); };
            FolderInput.KeyDown += async (sender, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; debounce.Stop(); await InspectSelected(); } };
            browse.Click += (sender, e) => { using (var dialog = new FolderBrowserDialog { Description = direct ? "เลือกโฟลเดอร์ Hogwarts Legacy ที่มี Phoenix" : "เลือกโฟลเดอร์ที่มี ArcaneMPClient.dll", ShowNewFolderButton = false }) { if (Directory.Exists(FolderInput.Text)) dialog.SelectedPath = FolderInput.Text; if (dialog.ShowDialog(this) == DialogResult.OK) FolderInput.Text = dialog.SelectedPath; } };
            chooseFile.Click += (sender, e) => { using (var dialog = new OpenFileDialog { Title = "เลือกตำแหน่งติดตั้ง", Filter = direct ? "Hogwarts Legacy|HogwartsLegacy.exe" : "ArcaneMP|ArcaneMPClient.dll;ArcaneMPLauncher.exe", CheckFileExists = true, RestoreDirectory = true }) { if (dialog.ShowDialog(this) == DialogResult.OK) FolderInput.Text = dialog.FileName; } };
            Install.Click += async (sender, e) => await Change(false);
            Remove.Click += async (sender, e) => await Change(true);
            openBackup.Click += (sender, e) => { if (!String.IsNullOrEmpty(backupPath) && Directory.Exists(Path.GetDirectoryName(backupPath))) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.GetDirectoryName(backupPath) + "\"") { UseShellExecute = true }); };
            processTimer.Tick += (sender, e) => { if (snapshot != null && !IsBusy && !inspecting && Visible && !preview) { bool previous = targetRunning; targetRunning = IsRunning(snapshot); UpdateActions(); if (previous != targetRunning) ShowReadiness(); } };
            processTimer.Start();
        }

        private void ResetRows() { MenuRow.SetState("รอตรวจโฟลเดอร์", Ui.Muted); GameRow.SetState("รอตรวจโฟลเดอร์", Ui.Muted); }
        private void SelectionChanged() { if (!IsBusy) { UpdateActions(); if (snapshot != null && !inspecting) ShowReadiness(); } }

        private InstallSnapshot ReadSnapshot(string path)
        {
            var result = new InstallSnapshot();
            if (!direct) {
                try { result.Menu = PatchEngine.Inspect(path); result.Root = Path.GetDirectoryName(result.Menu.TargetPath); }
                catch (Exception error) { result.MenuError = error.Message; }
            }
            try { result.Game = GameThaiEngine.Inspect(path, direct ? "game" : "arcanemp"); result.Root = result.Game.RootPath; }
            catch (Exception error) { result.GameError = error.Message; }
            if (direct) result.GameRoot = result.Root;
            else if (result.Root != null) {
                try {
                    string config = Path.Combine(result.Root, "ArcaneMP_launcher.json");
                    if (File.Exists(config)) { var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(config)); object value; if (data != null && data.TryGetValue("game_path", out value) && !String.IsNullOrWhiteSpace(Convert.ToString(value))) result.GameRoot = Path.GetFullPath(Convert.ToString(value)); }
                } catch (Exception) { /* Store lookup is optional; the engine still validates before writes. */ }
            }
            return result;
        }

        private async Task InspectSelected()
        {
            if (IsBusy || preview || IsDisposed) return;
            int observed = ++revision; string path = FolderInput.Text;
            if (String.IsNullOrWhiteSpace(path)) { snapshot = null; inspecting = false; progress.Visible = false; SetStatus("เลือกโฟลเดอร์เพื่อเริ่มต้น", direct ? "เลือกโฟลเดอร์ Hogwarts Legacy ที่มี Phoenix" : "เลือกโฟลเดอร์ ArcaneMP แล้วเลือกเมนู ตัวเกม หรือทั้งสองส่วน", Ui.Gold); UpdateActions(); return; }
            inspecting = true; UpdateActions();
            progress.Visible = true; SetStatus("กำลังตรวจไฟล์", "ตรวจสถานะเมนูและแพ็กภาษาไทยในตำแหน่งที่เลือก", Ui.Gold);
            InstallSnapshot result = await Task.Run(() => ReadSnapshot(path));
            if (IsDisposed || observed != revision) return;
            snapshot = result; inspecting = false; targetRunning = IsRunning(result); ApplyRows(); progress.Visible = false; UpdateActions(); ShowReadiness();
        }

        private void ApplyRows()
        {
            if (!direct) MenuRow.SetState(snapshot.Menu == null ? "ยังไม่พร้อม" : snapshot.Menu.IsInstalled ? "ติดตั้งแล้ว" : snapshot.Menu.NeedsRefresh ? "พร้อมย้าย / อัปเดต" : "พร้อมติดตั้ง", snapshot.Menu == null ? Ui.Red : snapshot.Menu.IsInstalled ? Ui.Green : Ui.Gold);
            GameRow.SetState(snapshot.Game == null ? "ยังไม่พร้อม" : snapshot.Game.HasConflict ? "พบไฟล์ชื่อซ้ำ" : snapshot.Game.IsInstalled ? "ติดตั้งแล้ว" : snapshot.Game.NeedsMigration ? "พร้อมย้ายเข้า mods" : "พร้อมติดตั้ง · 72%", snapshot.Game == null || snapshot.Game.HasConflict ? Ui.Red : snapshot.Game.IsInstalled ? Ui.Green : Ui.Gold);
        }

        private string SelectedError()
        {
            if (snapshot == null) return "";
            if (MenuRow.Choice.Checked && !direct && snapshot.MenuError != null) return snapshot.MenuError;
            if (GameRow.Choice.Checked && snapshot.GameError != null) return snapshot.GameError;
            if (GameRow.Choice.Checked && snapshot.Game != null && snapshot.Game.HasConflict) return snapshot.Game.Message;
            return "";
        }

        private int PendingCount() { if (snapshot == null) return 0; return (!direct && MenuRow.Choice.Checked && snapshot.Menu != null && !snapshot.Menu.IsInstalled ? 1 : 0) + (GameRow.Choice.Checked && snapshot.Game != null && !snapshot.Game.IsInstalled ? 1 : 0); }
        private int SelectedCount() { return (!direct && MenuRow.Choice.Checked ? 1 : 0) + (GameRow.Choice.Checked ? 1 : 0); }
        private void UpdateActions()
        {
            bool valid = snapshot != null && SelectedError() == "" && SelectedCount() > 0;
            Install.Enabled = !IsBusy && !inspecting && !targetRunning && valid && PendingCount() > 0;
            Remove.Enabled = !IsBusy && !inspecting && !targetRunning && valid && ((!direct && MenuRow.Choice.Checked && snapshot.Menu != null && snapshot.Menu.CanRestore) || (GameRow.Choice.Checked && snapshot.Game != null && snapshot.Game.CanUninstall));
            selected.Text = "เลือก " + SelectedCount() + " ส่วน";
            processNotice.Text = targetRunning ? "เกมหรือ Launcher ของตำแหน่งนี้ยังเปิดอยู่ กรุณาปิดก่อนจัดการไฟล์" : direct ? "ใช้ภาษา English ของตัวเกม แล้วเปิดเกมใหม่" : "จัดเก็บมอดใน mods\\ArcaneThai  ·  ใช้ภาษา English ของตัวเกม";
            processNotice.ForeColor = targetRunning ? Ui.Gold : Ui.Muted;
        }

        private void ShowReadiness()
        {
            string error = SelectedError();
            if (error != "") SetStatus("ตรวจตำแหน่งหรือไฟล์ก่อนติดตั้ง", error, Ui.Red);
            else if (SelectedCount() == 0) SetStatus("เลือกอย่างน้อยหนึ่งส่วน", "ติ๊กเมนู ArcaneMP หรือข้อความตัวเกมที่ต้องการจัดการ", Ui.Gold);
            else if (targetRunning) SetStatus("รอปิดเกมและ Launcher", "ตรวจไฟล์เรียบร้อยแล้ว ดูภาพตัวอย่างได้ระหว่างรอ ปิดเกมแล้วปุ่มจะพร้อมจัดการไฟล์", Ui.Gold);
            else if (snapshot != null && snapshot.Root != null) SetStatus(PendingCount() == 0 ? "ส่วนที่เลือกติดตั้งไว้แล้ว" : "พร้อมติดตั้ง " + PendingCount() + " ส่วน", targetRunning ? "ตรวจไฟล์เสร็จแล้ว กรุณาปิดเกมและ Launcher ก่อนเริ่ม" : PendingCount() == 0 ? "เปิดเกมใหม่ได้ หรือเลือกคืนค่า / ถอนแพ็กเฉพาะส่วนที่ต้องการ" : direct ? "ติดตั้งเป็นแพ็กเสริมในโฟลเดอร์เกมที่เลือก" : "เมนูและตัวเกมใช้โฟลเดอร์นี้ร่วมกัน โปรแกรมจะสำรองและตรวจไฟล์ให้", PendingCount() == 0 ? Ui.Green : Ui.Gold);
        }

        private async Task Change(bool removing)
        {
            if (IsBusy || snapshot == null || targetRunning) return;
            string root = snapshot.Root;
            bool menu = !direct && MenuRow.Choice.Checked, game = GameRow.Choice.Checked;
            IsBusy = true; revision++; inspecting = false; debounce.Stop(); SetInputs(false); progress.Visible = true;
            var completed = new List<string>(); string failure = null;
            try {
                snapshot = await Task.Run(() => ReadSnapshot(root));
                if (SelectedError() != "") throw new InvalidOperationException(SelectedError());
                if (IsRunning(snapshot)) throw new IOException("ปิดเกมและ ArcaneMP Launcher ก่อนทำต่อ");
                if (menu && (removing ? snapshot.Menu.CanRestore : !snapshot.Menu.IsInstalled)) {
                    SetStatus(removing ? "กำลังคืนค่าเมนู ArcaneMP" : "กำลังติดตั้งเมนู ArcaneMP", "กำลังสำรองและตรวจสอบไฟล์ กรุณารอสักครู่", Ui.Gold);
                    OperationResult result = await Task.Run(() => removing ? PatchEngine.Restore(root) : PatchEngine.Install(root));
                    if (result.BackupPath != null) backupPath = result.BackupPath;
                    completed.Add("เมนู ArcaneMP");
                }
                if (game && (removing ? snapshot.Game.CanUninstall : !snapshot.Game.IsInstalled)) {
                    SetStatus(removing ? "กำลังถอนแพ็กตัวเกม" : "กำลังติดตั้งข้อความตัวเกม", "กำลังตรวจแพ็กและจุดโหลด กรุณารอสักครู่", Ui.Gold);
                    GameThaiResult result = await Task.Run(() => removing ? GameThaiEngine.Uninstall(root, direct ? "game" : "arcanemp") : GameThaiEngine.Install(root, direct ? "game" : "arcanemp"));
                    if (result.BackupPath != null) backupPath = result.BackupPath;
                    completed.Add("ข้อความตัวเกม");
                }
            } catch (Exception error) { failure = error is UnauthorizedAccessException ? "ไม่มีสิทธิ์เขียนโฟลเดอร์นี้ โปรดเลือกตำแหน่งที่มีสิทธิ์ หรือเปิดด้วยสิทธิ์ผู้ดูแลระบบ" : error.Message; }
            {
                snapshot = await Task.Run(() => ReadSnapshot(root));
                IsBusy = false; targetRunning = IsRunning(snapshot); progress.Visible = false; ApplyRows(); SetInputs(true); UpdateActions(); openBackup.Visible = backupPath != null;
                if (failure != null) SetStatus(completed.Count == 0 ? "ยังดำเนินการไม่ได้" : "ดำเนินการสำเร็จบางส่วน", (completed.Count == 0 ? "" : "สำเร็จ: " + String.Join(", ", completed) + "\n") + failure, Ui.Red);
                else SetStatus(removing ? "คืนค่า / ถอนแพ็กเรียบร้อย" : "ติดตั้งภาษาไทยเรียบร้อย", String.Join(" และ ", completed) + (removing ? " กลับสู่สภาพก่อนติดตั้งแล้ว เก็บไฟล์สำรองไว้ให้" : " พร้อมใช้งาน เปิดเกมใหม่ได้โดยใช้ภาษา English ของเกม"), Ui.Green);
            }
        }

        private void SetInputs(bool enabled) { FolderInput.Enabled = browse.Enabled = scan.Enabled = openBackup.Enabled = enabled; MenuRow.Choice.Enabled = enabled && !direct; GameRow.Choice.Enabled = enabled && !direct; Install.Enabled = Remove.Enabled = false; foreach (Control c in ((Control)scan.Parent).Controls) c.Enabled = enabled; }
        private void SetStatus(string title, string detail, Color color) { statusTitle.Text = title; statusTitle.ForeColor = color; statusDetail.Text = detail; tips.SetToolTip(statusDetail, detail); }
        private static bool IsRunning(InstallSnapshot state)
        {
            if (state == null || state.Root == null) return false;
            foreach (string name in new[] { "ArcaneMPLauncher", "HogwartsLegacy" }) foreach (Process process in Process.GetProcessesByName(name)) using (process) {
                try {
                    string path = Path.GetFullPath(process.MainModule.FileName);
                    if (String.Equals(path, Path.Combine(state.Root, "ArcaneMPLauncher.exe"), StringComparison.OrdinalIgnoreCase)) return true;
                    if (state.GameRoot != null && path.StartsWith(state.GameRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && String.Equals(Path.GetFileName(path), "HogwartsLegacy.exe", StringComparison.OrdinalIgnoreCase)) return true;
                } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
            }
            return false;
        }

        public void Preview(string scene)
        {
            preview = true; debounce.Stop(); FolderInput.Text = direct ? @"D:\Epic Games\Hogwarts Legacy" : @"F:\ArcaneMP";
            snapshot = new InstallSnapshot { Root = FolderInput.Text, Menu = direct ? null : new Inspection(), Game = new GameThaiInspection() };
            if (scene == "installed") { if (snapshot.Menu != null) { snapshot.Menu.IsInstalled = true; snapshot.Menu.CanRestore = true; } snapshot.Game.IsInstalled = snapshot.Game.CanUninstall = true; }
            if (scene == "error") { snapshot.Game.HasConflict = true; snapshot.Game.Message = "พบแพ็กชื่อเดียวกันที่เนื้อหาต่างจากชุดนี้ เก็บไฟล์นั้นแยกไว้ก่อน แล้วกดตรวจใหม่"; }
            if (scene == "game") MenuRow.Choice.Checked = false;
            if (scene == "empty") { FolderInput.Text = ""; snapshot = null; ResetRows(); UpdateActions(); SetStatus("เลือกโฟลเดอร์เพื่อเริ่มต้น", "โปรแกรมจะตรวจเมนูและแพ็กตัวเกมให้จากโฟลเดอร์เดียวกัน", Ui.Gold); return; }
            ApplyRows(); UpdateActions(); ShowReadiness();
        }

        protected override void Dispose(bool disposing) { if (disposing) { debounce.Dispose(); processTimer.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
    }
}

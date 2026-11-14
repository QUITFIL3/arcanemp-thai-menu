using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArcaneThai
{
    internal sealed class InstallerForm : Form
    {
        internal readonly InstallPage ClientPage = new InstallPage(false);
        internal readonly InstallPage DirectPage = new InstallPage(true);
        private readonly Panel content = new Panel();
        private readonly ActionButton[] navigation = new ActionButton[4];
        private readonly Control[] pages = new Control[4];
        private bool rendering;
        private PictureBox galleryImage;
        private ComboBox galleryChoice;
        private readonly string[] previewNames = { "Play", "Voice", "Audio", "Graphics", "Owl", "About" };
        private readonly string[] previewTitles = { "หน้าเล่นและเลือกเซิร์ฟเวอร์", "แชตเสียง", "เสียงเกม", "กราฟิก", "ไปรษณีย์นกฮูก", "เกี่ยวกับ ArcaneMP" };
        public bool IsBusy { get { return ClientPage.IsBusy || DirectPage.IsBusy; } }

        public InstallerForm()
        {
            Text = "ArcaneThai | ตัวติดตั้งภาษาไทย 0.6.0";
            ClientSize = new Size(1020, 630); MinimumSize = new Size(936, 659);
            BackColor = Ui.Canvas; ForeColor = Ui.Text; Font = Ui.Font(); ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 212)); frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(frame);
            var rail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Padding = new Padding(20, 26, 20, 18), BackColor = Ui.Rail, Margin = Padding.Empty };
            int[] heights = { 44, 28, 38, 48, 48, 48, 48 };
            foreach (int value in heights) rail.RowStyles.Add(new RowStyle(SizeType.Absolute, value));
            rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            var brand = Ui.Label("ArcaneThai", 21, Ui.Gold); brand.Font = new Font("Georgia", 21);
            rail.Controls.Add(brand, 0, 0); rail.Controls.Add(Ui.Label("ภาษาไทยพร้อมออกเดินทาง", 9.5f, Ui.Muted), 0, 1);
            string[] labels = { "ติดตั้งให้ ArcaneMP", "ตัวเกม Steam / Epic", "ภาพตัวอย่าง", "คำแนะนำและเครดิต" };
            for (int index = 0; index < navigation.Length; index++) {
                int target = index;
                navigation[index] = new ActionButton { Text = labels[index], Navigation = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
                navigation[index].Click += (sender, e) => SelectPage(target);
                rail.Controls.Add(navigation[index], 0, index + 3);
            }
            rail.Controls.Add(Ui.Label("คำแปลตัวเกมประมาณ 72%", 9, Ui.Muted), 0, 8);
            rail.Controls.Add(Ui.Label("เวอร์ชัน 0.6.0", 9, Ui.Muted), 0, 9);
            frame.Controls.Add(rail, 0, 0);
            content.Dock = DockStyle.Fill; content.Margin = Padding.Empty;
            frame.Controls.Add(content, 1, 0);
            pages[0] = ClientPage; pages[1] = DirectPage; pages[2] = CreateGallery(); pages[3] = CreateHelp();
            foreach (Control page in pages) { page.Dock = DockStyle.Fill; page.Visible = false; content.Controls.Add(page); }
            SelectPage(0);
            FormClosing += (sender, e) => { if (IsBusy) e.Cancel = true; };
        }

        private void SelectPage(int selected)
        {
            if (IsBusy) return;
            for (int index = 0; index < pages.Length; index++) { pages[index].Visible = index == selected; navigation[index].Selected = index == selected; }
            pages[selected].BringToFront();
        }

        private Control CreateGallery()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(28, 22, 28, 20), BackColor = Ui.Canvas };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(Ui.Label("ตัวอย่างภาษาไทย", 22, Ui.Text, true), 0, 0);
            layout.Controls.Add(Ui.Label("ดูหน้าเมนูที่แปลอยู่ในแพ็ก ก่อนเริ่มติดตั้ง", 10.5f, Ui.Muted), 0, 1);
            galleryChoice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Ui.Font(), BackColor = Ui.Surface, ForeColor = Ui.Text, Width = 300, Margin = new Padding(0, 3, 0, 8), AccessibleName = "เลือกภาพตัวอย่าง" };
            galleryChoice.Items.AddRange(previewTitles);
            layout.Controls.Add(galleryChoice, 0, 2);
            galleryImage = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Ui.Rail, Margin = Padding.Empty };
            layout.Controls.Add(galleryImage, 0, 3);
            layout.Controls.Add(Ui.Label("ภาพจาก HTML ใน Chromium ใช้ข้อมูลตัวอย่าง ไม่ใช่ภาพระหว่างเล่น", 9.5f, Ui.Muted), 0, 4);
            var full = new ActionButton { Text = "เปิดภาพขนาดใหญ่", Width = 190, Dock = DockStyle.Left };
            layout.Controls.Add(full, 0, 5);
            galleryChoice.SelectedIndexChanged += (sender, e) => {
                Image prior = galleryImage.Image;
                galleryImage.Image = PreviewImage(galleryChoice.SelectedIndex);
                if (prior != null) prior.Dispose();
            };
            full.Click += (sender, e) => {
                var viewer = new Form { Text = previewTitles[galleryChoice.SelectedIndex], BackColor = Ui.Rail, ClientSize = new Size(1120, 630), StartPosition = FormStartPosition.CenterParent, ShowIcon = false };
                var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = PreviewImage(galleryChoice.SelectedIndex) };
                viewer.Controls.Add(picture); viewer.FormClosed += (s, args) => picture.Image.Dispose(); viewer.Show(this);
            };
            galleryChoice.SelectedIndex = 0;
            return layout;
        }

        private Image PreviewImage(int index)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ArcaneThai.Preview." + previewNames[index])) {
                if (stream == null) throw new InvalidDataException("Missing preview image.");
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }

        private Control CreateHelp()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 12, Padding = new Padding(28, 22, 28, 20), BackColor = Ui.Canvas, AutoScroll = true };
            int[] heights = { 46, 30, 32, 62, 32, 62, 32, 62, 40, 32, 48 };
            foreach (int value in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, value)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Ui.Label("คำแนะนำและเครดิต", 22, Ui.Text, true), 0, 0);
            layout.Controls.Add(Ui.Label("เรื่องที่ควรรู้ก่อนออกเดินทาง", 10.5f, Ui.Muted), 0, 1);
            layout.Controls.Add(Ui.Label("ใช้ภาษา English ของตัวเกม", 12, Ui.Gold, true), 0, 2);
            layout.Controls.Add(Ui.Label("แพ็กนี้แทนที่ข้อความอังกฤษด้วยไทยที่มีอยู่ประมาณ 72%\nคำแปลที่ยังไม่มี เสียงพากย์ และป้ายรูปภาพยังเป็นภาษาเดิม", 10.5f, Ui.Muted), 0, 3);
            layout.Controls.Add(Ui.Label("เมื่อ ArcaneMP อัปเดต", 12, Ui.Gold, true), 0, 4);
            layout.Controls.Add(Ui.Label("เปิดตัวติดตั้งเพื่อตรวจและติดตั้งเมนูไทยซ้ำให้ตรงกับ client รุ่นใหม่\nไฟล์ทางการยังอยู่เหมือนเดิม มีสำเนาก่อนเปลี่ยนและปุ่มคืนค่า", 10.5f, Ui.Muted), 0, 5);
            layout.Controls.Add(Ui.Label("โฟลเดอร์มอดภาษาไทย", 12, Ui.Gold, true), 0, 6);
            layout.Controls.Add(Ui.Label("เก็บ DLL และแพ็กตัวเกมที่ ArcaneMP\\mods\\ArcaneThai\nแพ็กใช้ hard link บน NTFS เพื่อให้จุดสแกนเดิมโหลดไฟล์เดียวกัน", 10.5f, Ui.Muted), 0, 7);
            var open = new ActionButton { Text = "เปิดโฟลเดอร์มอด", Width = 190, Dock = DockStyle.Left };
            open.Click += (sender, e) => { string root = ClientPage.CurrentRoot; string folder = root == "" ? "" : ModStorage.DirectoryFor(root); if (Directory.Exists(folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true }); else MessageBox.Show(this, "เลือกโฟลเดอร์ ArcaneMP และติดตั้งภาษาไทยก่อน", "ยังไม่พบโฟลเดอร์มอด", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            layout.Controls.Add(open, 0, 8);
            layout.Controls.Add(Ui.Label("คำแปลและฟอนต์", 12, Ui.Gold, true), 0, 9);
            var credits = new ActionButton { Text = "อ่านเครดิตและใบอนุญาต", Width = 224, Dock = DockStyle.Left };
            credits.Click += (sender, e) => {
                var dialog = new Form { Text = "เครดิตและใบอนุญาต", Size = new Size(800, 600), StartPosition = FormStartPosition.CenterParent, ShowIcon = false };
                dialog.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = Ui.Font(), ForeColor = Ui.Text, BackColor = Ui.Rail, BorderStyle = BorderStyle.None, Text = GameThaiEngine.Credits + "\n\n" + PatchEngine.FontLicenseText }); dialog.Show(this);
            };
            layout.Controls.Add(credits, 0, 10);
            return layout;
        }

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int enabled = 1; DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }

        public void RenderPreview(string output, string scene)
        {
            rendering = true; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-20000, -20000);
            if (scene == "game-direct") { SelectPage(1); DirectPage.Preview("ready"); }
            else if (scene == "gallery") SelectPage(2);
            else if (scene == "help") SelectPage(3);
            else { SelectPage(0); ClientPage.Preview(scene); }
            if (scene == "minimum") ClientSize = new Size(920, 620);
            Show(); Application.DoEvents(); PerformLayout();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
            Hide();
        }

        protected override bool ShowWithoutActivation { get { return rendering; } }
        protected override void Dispose(bool disposing) { if (disposing && galleryImage != null && galleryImage.Image != null) galleryImage.Image.Dispose(); base.Dispose(disposing); }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace ArcaneThai
{
    public sealed class GameThaiInspection
    {
        public string RootPath;
        public string TargetPath;
        public string Mode;
        public bool IsInstalled;
        public bool HasConflict;
        public string Message;
        public int ThaiEntries = 40520;
        public int TotalEntries = 56112;
    }

    public sealed class GameThaiResult
    {
        public string TargetPath;
        public string BackupPath;
        public string Sha256;
        public bool Changed;
        public string Message;
    }

    public static class GameThaiEngine
    {
        public const string PakName = "zArcaneThai_Community_P.pak";
        public const string ExpectedSha256 = "164f13205cd5d9968856a3ce7d3dd7b749c55779705a2610a76a9fecff37a565";
        public const int ExpectedBytes = 3903569;

        private static string FullPath(string selected)
        {
            if (String.IsNullOrWhiteSpace(selected)) throw new InvalidOperationException("เลือกโฟลเดอร์ที่จะติดตั้งภาษาไทยก่อน");
            return Path.GetFullPath(selected.Trim().Trim('"'));
        }

        private static string ResolveRoot(string selected, string mode)
        {
            string root = FullPath(selected);
            if (mode == "arcanemp") return Path.GetDirectoryName(PatchEngine.ResolveTarget(root));
            if (mode != "game") throw new InvalidOperationException("เลือกติดตั้งสำหรับ ArcaneMP หรือตัวเกมปกติ");
            if (File.Exists(root)) {
                if (!String.Equals(Path.GetFileName(root), "HogwartsLegacy.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("เลือกโฟลเดอร์ Hogwarts Legacy หรือไฟล์ HogwartsLegacy.exe");
                root = Path.GetDirectoryName(root);
            }
            for (int depth = 0; depth < 5 && root != null; depth++, root = Path.GetDirectoryName(root)) {
                if (Directory.Exists(Path.Combine(root, "Phoenix", "Content", "Paks"))
                    && File.Exists(Path.Combine(root, "Phoenix", "Binaries", "Win64", "HogwartsLegacy.exe"))) return root;
            }
            throw new InvalidOperationException("ไม่พบโฟลเดอร์เกมที่มี Phoenix\\Content\\Paks และ Phoenix\\Binaries\\Win64\\HogwartsLegacy.exe");
        }

        public static GameThaiInspection Inspect(string selected, string mode)
        {
            string root = ResolveRoot(selected, mode);
            string target = mode == "arcanemp"
                ? Path.Combine(root, "files", "paks", PakName)
                : Path.Combine(root, "Phoenix", "Content", "Paks", "~mods", PakName);
            bool exists = File.Exists(target);
            bool installed = exists && new FileInfo(target).Length == ExpectedBytes && HashFile(target) == ExpectedSha256;
            return new GameThaiInspection {
                RootPath = root, TargetPath = target, Mode = mode, IsInstalled = installed, HasConflict = exists && !installed,
                Message = exists && !installed ? "มีแพ็กชื่อเดียวกันที่เนื้อหาต่างจากชุดนี้ จึงไม่เขียนทับ โปรดเก็บไฟล์นั้นแยกไว้ก่อน"
                    : installed ? "พบแพ็กภาษาไทยชุดนี้แล้ว เลือกถอนแพ็กได้โดยเก็บสำเนาไว้"
                    : mode == "arcanemp" ? "ติดตั้งใน client นี้ ใช้แปลตัวเกมเมื่อเปิดผ่าน ArcaneMP"
                    : "ติดตั้งเป็นแพ็กเสริมในโฟลเดอร์เกม ใช้กับ Steam หรือ Epic ได้ตามโครงสร้างที่รองรับ"
            };
        }

        public static GameThaiResult Install(string selected, string mode)
        {
            GameThaiInspection info = Inspect(selected, mode);
            AssertClosed(info);
            if (info.HasConflict) throw new InvalidOperationException(info.Message);
            if (info.IsInstalled) return new GameThaiResult {
                TargetPath = info.TargetPath, Sha256 = ExpectedSha256, Changed = false, Message = "ติดตั้งแพ็กภาษาไทยตัวเกมชุดนี้ไว้แล้ว"
            };
            byte[] bytes = Resource("ArcaneThai.GamePak");
            if (bytes.Length != ExpectedBytes || PatchEngine.Hash(bytes) != ExpectedSha256)
                throw new InvalidDataException("แพ็กภาษาไทยในตัวติดตั้งตรวจสอบไม่ผ่าน กรุณาดาวน์โหลด EXE ใหม่");
            string directory = Path.GetDirectoryName(info.TargetPath);
            AssertNoReparsePoints(info.RootPath, directory);
            Directory.CreateDirectory(directory);
            string stage = info.TargetPath + "." + Guid.NewGuid().ToString("N") + ".arcane-thai-stage";
            try {
                using (var file = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (HashFile(stage) != ExpectedSha256) throw new IOException("ตรวจแพ็กที่เตรียมติดตั้งไม่ผ่าน");
                AssertClosed(info);
                // Move without replacement: a file arriving during installation is preserved.
                File.Move(stage, info.TargetPath);
            } finally { if (File.Exists(stage)) File.Delete(stage); }
            if (HashFile(info.TargetPath) != ExpectedSha256) throw new IOException("ตรวจแพ็กหลังติดตั้งไม่ผ่าน");
            return new GameThaiResult {
                TargetPath = info.TargetPath, Sha256 = ExpectedSha256, Changed = true,
                Message = "ติดตั้งแพ็กตัวเกมแล้ว ใช้ภาษา English ของเกม และเปิดเกมใหม่" + (mode == "arcanemp" ? "ผ่าน ArcaneMP" : "")
            };
        }

        public static GameThaiResult Uninstall(string selected, string mode)
        {
            GameThaiInspection info = Inspect(selected, mode);
            AssertClosed(info);
            if (info.HasConflict) throw new InvalidOperationException("แพ็กถูกเปลี่ยนภายหลัง จึงไม่ย้ายหรือถอนไฟล์นั้น");
            if (!info.IsInstalled) return new GameThaiResult { TargetPath = info.TargetPath, Changed = false, Message = "ไม่พบแพ็กภาษาไทยชุดนี้ในตำแหน่งที่เลือก" };
            string directory = Path.Combine(info.RootPath, "thai-pack-backups", "disabled-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            AssertNoReparsePoints(info.RootPath, Path.GetDirectoryName(info.TargetPath));
            AssertNoReparsePoints(info.RootPath, directory);
            Directory.CreateDirectory(directory);
            string backup = Path.Combine(directory, PakName);
            using (var file = new FileStream(info.TargetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                if (file.Length != ExpectedBytes) throw new IOException("แพ็กถูกเปลี่ยนระหว่างทำงาน");
                using (var sha = System.Security.Cryptography.SHA256.Create()) {
                    string hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                    if (hash != ExpectedSha256) throw new IOException("แพ็กถูกเปลี่ยนระหว่างทำงาน");
                }
            }
            File.Move(info.TargetPath, backup);
            if (HashFile(backup) != ExpectedSha256) throw new IOException("แพ็กถูกแก้ไขระหว่างทำงาน เก็บไฟล์ที่ย้ายไว้ที่ " + backup);
            return new GameThaiResult { TargetPath = info.TargetPath, BackupPath = backup, Sha256 = ExpectedSha256, Changed = true, Message = "ถอนแพ็กตัวเกมแล้ว และเก็บไฟล์ไว้ใน thai-pack-backups" };
        }

        private static void AssertClosed(GameThaiInspection info)
        {
            string gameRoot = info.Mode == "game" ? info.RootPath : "";
            string configPath = Path.Combine(info.RootPath, "ArcaneMP_launcher.json");
            if (info.Mode == "arcanemp" && File.Exists(configPath)) {
                try {
                    var config = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(configPath));
                    object value;
                    if (config != null && config.TryGetValue("game_path", out value) && !String.IsNullOrWhiteSpace(Convert.ToString(value))) gameRoot = FullPath(Convert.ToString(value));
                } catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
            foreach (string name in new[] { "ArcaneMPLauncher", "HogwartsLegacy" }) {
                foreach (Process process in Process.GetProcessesByName(name)) {
                    using (process) {
                        string path;
                        try { path = Path.GetFullPath(process.MainModule.FileName); }
                        catch (System.ComponentModel.Win32Exception) { throw new IOException("ตรวจสถานะเกมไม่ได้ โปรดปิดเกมและ ArcaneMP ก่อนติดตั้งหรือถอนแพ็ก"); }
                        catch (InvalidOperationException) { continue; }
                        bool game = String.Equals(Path.GetFileName(path), "HogwartsLegacy.exe", StringComparison.OrdinalIgnoreCase)
                            && (gameRoot == "" || path.StartsWith(gameRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                        bool launcher = info.Mode == "arcanemp" && String.Equals(path, Path.Combine(info.RootPath, "ArcaneMPLauncher.exe"), StringComparison.OrdinalIgnoreCase);
                        if (game || launcher) throw new IOException("เกมหรือ Launcher ยังเปิดอยู่ โปรดปิด Hogwarts Legacy และ ArcaneMP ก่อนติดตั้งหรือถอนแพ็ก");
                    }
                }
            }
            if (File.Exists(info.TargetPath)) {
                try { using (var file = new FileStream(info.TargetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
                catch (IOException) { throw new IOException("แพ็กกำลังใช้งานอยู่ โปรดปิดเกมก่อนทำต่อ"); }
            }
        }

        private static void AssertNoReparsePoints(string root, string directory)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(directory);
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !String.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("ตำแหน่งติดตั้งอยู่นอกโฟลเดอร์ที่เลือก");
            while (!String.Equals(path, root, StringComparison.OrdinalIgnoreCase)) {
                if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("โฟลเดอร์ปลายทางเป็นลิงก์ไปตำแหน่งอื่น โปรดเลือกโฟลเดอร์จริง");
                path = Path.GetDirectoryName(path);
            }
        }

        private static string HashFile(string path)
        {
            using (var file = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        private static byte[] Resource(string name)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if (source == null) throw new InvalidDataException("Missing bundled resource: " + name);
                using (var data = new MemoryStream()) { source.CopyTo(data); return data.ToArray(); }
            }
        }

        public static string Credits { get { return System.Text.Encoding.UTF8.GetString(Resource("ArcaneThai.GameCredits")); } }
    }
}

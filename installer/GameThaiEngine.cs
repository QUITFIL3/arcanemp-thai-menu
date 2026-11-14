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
        public string LoaderPath;
        public string Mode;
        public bool IsInstalled;
        public bool HasConflict;
        public bool LoaderLinked;
        public bool CanUninstall;
        public bool NeedsMigration;
        public string Message;
        public int ThaiEntries = 40520;
        public int TotalEntries = 56112;
    }

    public sealed class GameThaiResult
    {
        public string TargetPath;
        public string LoaderPath;
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
                ? Path.Combine(ModStorage.DirectoryFor(root), PakName)
                : Path.Combine(root, "Phoenix", "Content", "Paks", "~mods", PakName);
            string loader = mode == "arcanemp" ? Path.Combine(root, "files", "paks", PakName) : target;
            bool owned = IsOwned(target), loaderOwned = IsOwned(loader);
            bool conflict = (File.Exists(target) && !owned) || (File.Exists(loader) && !loaderOwned);
            bool linked = owned && loaderOwned && (mode == "game" || ModStorage.SameFile(target, loader));
            bool installed = !conflict && linked;
            bool migrating = mode == "arcanemp" && !conflict && !installed && (owned || loaderOwned);
            return new GameThaiInspection {
                RootPath = root, TargetPath = target, LoaderPath = loader, Mode = mode, IsInstalled = installed, HasConflict = conflict,
                LoaderLinked = linked, CanUninstall = !conflict && (owned || loaderOwned), NeedsMigration = migrating,
                Message = conflict ? "มีแพ็กชื่อเดียวกันที่เนื้อหาต่างจากชุดนี้ จึงไม่เขียนทับ โปรดเก็บไฟล์นั้นแยกไว้ก่อน"
                    : migrating ? "พร้อมย้ายหรือเชื่อมแพ็กเดิมให้โหลดจาก mods\\ArcaneThai"
                    : installed ? "พบแพ็กภาษาไทยชุดนี้แล้ว เลือกถอนแพ็กได้โดยเก็บสำเนาไว้"
                    : mode == "arcanemp" ? "เก็บแพ็กใน mods\\ArcaneThai และเชื่อมให้ ArcaneMP โหลดไฟล์เดียวกัน"
                    : "ติดตั้งเป็นแพ็กเสริมในโฟลเดอร์เกม ใช้กับ Steam หรือ Epic ได้ตามโครงสร้างที่รองรับ"
            };
        }

        public static GameThaiResult Install(string selected, string mode)
        {
            GameThaiInspection info = Inspect(selected, mode);
            AssertClosed(info);
            if (info.HasConflict) throw new InvalidOperationException(info.Message);
            if (info.IsInstalled) return new GameThaiResult {
                TargetPath = info.TargetPath, LoaderPath = info.LoaderPath, Sha256 = ExpectedSha256, Changed = false, Message = "ติดตั้งแพ็กภาษาไทยตัวเกมชุดนี้ไว้แล้ว"
            };
            bool createdPrimary = false, createdLink = false;
            string priorLoaderBackup = null;
            try {
                if (!File.Exists(info.TargetPath)) { WritePack(info); createdPrimary = true; }
                if (mode == "arcanemp") {
                    ModStorage.EnsureDirectory(info.RootPath, Path.GetDirectoryName(info.LoaderPath));
                    AssertClosed(info);
                    if (File.Exists(info.LoaderPath)) {
                        if (!IsOwned(info.LoaderPath)) throw new IOException("ไฟล์จุดโหลดถูกแก้ไขระหว่างทำงาน");
                        priorLoaderBackup = NewBackupPath(info.RootPath, "migrated");
                        File.Move(info.LoaderPath, priorLoaderBackup);
                    }
                    ModStorage.Link(info.LoaderPath, info.TargetPath);
                    createdLink = true;
                    if (!ModStorage.SameFile(info.LoaderPath, info.TargetPath)) throw new IOException("ลิงก์ไม่ได้ชี้ไปยังแพ็กใน mods");
                }
                if (!IsOwned(info.TargetPath) || !IsOwned(info.LoaderPath)) throw new IOException("ตรวจแพ็กหลังติดตั้งไม่ผ่าน");
            } catch {
                if (createdLink && ModStorage.SameFile(info.LoaderPath, info.TargetPath)) File.Delete(info.LoaderPath);
                if (priorLoaderBackup != null && !File.Exists(info.LoaderPath)) File.Move(priorLoaderBackup, info.LoaderPath);
                if (createdPrimary && IsOwned(info.TargetPath)) File.Delete(info.TargetPath);
                throw;
            }
            return new GameThaiResult {
                TargetPath = info.TargetPath, LoaderPath = info.LoaderPath, BackupPath = priorLoaderBackup, Sha256 = ExpectedSha256, Changed = true,
                Message = mode == "arcanemp" ? "ติดตั้งใน mods\\ArcaneThai และเชื่อมจุดโหลดแล้ว ใช้ภาษา English และเปิด ArcaneMP ใหม่"
                    : "ติดตั้งแพ็กตัวเกมแล้ว ใช้ภาษา English ของเกม และเปิดเกมใหม่"
            };
        }

        private static void WritePack(GameThaiInspection info)
        {
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
        }

        public static GameThaiResult Uninstall(string selected, string mode)
        {
            GameThaiInspection info = Inspect(selected, mode);
            AssertClosed(info);
            if (info.HasConflict) throw new InvalidOperationException("แพ็กถูกเปลี่ยนภายหลัง จึงไม่ย้ายหรือถอนไฟล์นั้น");
            if (!info.CanUninstall) return new GameThaiResult { TargetPath = info.TargetPath, LoaderPath = info.LoaderPath, Changed = false, Message = "ไม่พบแพ็กภาษาไทยชุดนี้ในตำแหน่งที่เลือก" };
            AssertNoReparsePoints(info.RootPath, Path.GetDirectoryName(info.TargetPath));
            AssertNoReparsePoints(info.RootPath, Path.GetDirectoryName(info.LoaderPath));
            string backup = NewBackupPath(info.RootPath, "disabled");
            string source = IsOwned(info.TargetPath) ? info.TargetPath : info.LoaderPath;
            File.Copy(source, backup);
            if (HashFile(backup) != ExpectedSha256) throw new IOException("แพ็กถูกแก้ไขระหว่างทำงาน เก็บไฟล์ที่ย้ายไว้ที่ " + backup);
            AssertClosed(info);
            if ((File.Exists(info.TargetPath) && !IsOwned(info.TargetPath)) || (File.Exists(info.LoaderPath) && !IsOwned(info.LoaderPath)))
                throw new IOException("แพ็กถูกเปลี่ยนระหว่างทำงาน เก็บสำเนาเดิมไว้ที่ " + backup);
            if (info.LoaderPath != info.TargetPath && File.Exists(info.LoaderPath)) File.Delete(info.LoaderPath);
            if (File.Exists(info.TargetPath)) File.Delete(info.TargetPath);
            return new GameThaiResult { TargetPath = info.TargetPath, LoaderPath = info.LoaderPath, BackupPath = backup, Sha256 = ExpectedSha256, Changed = true, Message = "ถอนแพ็กตัวเกมและลิงก์โหลดแล้ว เก็บไฟล์ไว้ใน thai-pack-backups" };
        }

        private static string NewBackupPath(string root, string prefix)
        {
            string directory = Path.Combine(root, "thai-pack-backups", prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            AssertNoReparsePoints(root, directory);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, PakName);
        }

        private static bool IsOwned(string path) { return File.Exists(path) && new FileInfo(path).Length == ExpectedBytes && HashFile(path) == ExpectedSha256; }

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
            foreach (string target in new[] { info.TargetPath, info.LoaderPath }) {
                if (!File.Exists(target)) continue;
                try { using (var file = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
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

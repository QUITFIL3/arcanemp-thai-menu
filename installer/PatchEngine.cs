using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ArcaneThai
{
    public sealed class Inspection
    {
        public string TargetPath;
        public string Sha256;
        public string FileVersion;
        public int MenuOffset;
        public int MenuLength;
        public bool IsInstalled;
        public bool HasPreviousThaiPatch;
        public bool CanRestore;
        public bool NeedsRefresh;
        public string LocalizedPath;
        internal string Html;
    }

    public sealed class OperationResult
    {
        public string TargetPath;
        public string BackupPath;
        public string BeforeSha256;
        public string AfterSha256;
        public string FileVersionBefore;
        public string FileVersionAfter;
        public bool Changed;
        public bool OutsideMenuUnchanged;
        public bool OfficialFileUnchanged;
        public string LocalizedPath;
        public string Message;
    }

    internal sealed class MenuBlock
    {
        public int Offset;
        public int Length;
        public string Html;
    }

    internal sealed class Backup
    {
        public string Path;
        public string OriginalSha256;
        public string PatchedSha256;
    }

    public static class PatchEngine
    {
        private const string StartMarker = "<!-- arcane-thai-menu:runtime-v2:start -->";
        private const string EndMarker = "<!-- arcane-thai-menu:runtime-v2:end -->";
        private const int MaxHtmlBytes = 16 * 1024 * 1024;
        private const string ThaiDll = "ArcaneMPClient.Thai.dll";
        private const string StateFile = "arcane-thai-menu.json";
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = MaxHtmlBytes };

        public static string ResolveTarget(string selected)
        {
            if (String.IsNullOrWhiteSpace(selected)) throw new InvalidOperationException("เลือกโฟลเดอร์ client หรือไฟล์ ArcaneMPClient.dll ก่อน");
            string candidate = System.IO.Path.GetFullPath(selected.Trim().Trim('"'));
            if (Directory.Exists(candidate)) candidate = System.IO.Path.Combine(candidate, "ArcaneMPClient.dll");
            else if (String.Equals(System.IO.Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
                candidate = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(candidate), "ArcaneMPClient.dll");
            if (!String.Equals(System.IO.Path.GetFileName(candidate), "ArcaneMPClient.dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("เลือก ArcaneMPClient.dll หรือ ArcaneMPLauncher.exe ในโฟลเดอร์ client");
            if (!File.Exists(candidate)) throw new FileNotFoundException("ไม่พบ ArcaneMPClient.dll ในตำแหน่งที่เลือก", candidate);
            return candidate;
        }

        public static Inspection Inspect(string selected)
        {
            string target = ResolveTarget(selected);
            byte[] bytes = ReadClient(target);
            MenuBlock block = LocateMenu(bytes);
            string sha = Hash(bytes);
            string directory = System.IO.Path.GetDirectoryName(target);
            string localized = System.IO.Path.Combine(directory, ThaiDll);
            var state = ReadState(directory);
            var config = ReadConfig(directory);
            bool selectedThai = Value(config, "mod_dll_name") == ThaiDll;
            bool installed = false;
            if (state != null && selectedThai && File.Exists(localized)) {
                byte[] translated = ReadClient(localized);
                installed = Value(state, "sourceSha256") == sha && Value(state, "localizedSha256") == Hash(translated)
                    && LocateMenu(translated).Html.Contains(CatalogStamp());
            }
            return new Inspection {
                TargetPath = target, Sha256 = sha, FileVersion = VersionOf(target),
                MenuOffset = block.Offset, MenuLength = block.Length, Html = block.Html,
                IsInstalled = installed, LocalizedPath = localized,
                NeedsRefresh = state != null && !installed,
                HasPreviousThaiPatch = block.Html.Contains("lang=\"th\"") || block.Html.Contains(StartMarker),
                CanRestore = (state != null && selectedThai) || FindBackup(target, sha) != null
            };
        }

        public static OperationResult Install(string selected)
        {
            string target = ResolveTarget(selected);
            AssertLauncherClosed(target);
            AssertWritableAndClosed(target);
            byte[] original = ReadClient(target);
            string before = Hash(original);
            string versionBefore = VersionOf(target);
            string directory = System.IO.Path.GetDirectoryName(target);
            string localized = System.IO.Path.Combine(directory, ThaiDll);
            var config = ReadConfig(directory);
            var previousState = ReadState(directory);
            if (File.Exists(localized) && previousState == null)
                throw new InvalidOperationException("มีไฟล์ ArcaneMPClient.Thai.dll อยู่แล้วแต่ไม่พบข้อมูลการติดตั้ง จึงไม่เขียนทับไฟล์นั้น");
            string modName = Value(config, "mod_dll_name");
            if (modName != "" && modName != "ArcaneMPClient.dll" && modName != ThaiDll)
                throw new InvalidOperationException("Launcher กำลังใช้ DLL ของมอดอื่น โปรดคืนค่ามอดนั้นก่อนติดตั้งภาษาไทย");
            if (modName == ThaiDll && previousState == null)
                throw new InvalidOperationException("ไม่พบข้อมูลการติดตั้งเดิม จึงยังเปลี่ยนการตั้งค่า Launcher ไม่ได้");
            byte[] source = OfficialSource(target, original);
            string sourceHash = Hash(source);
            MenuBlock block = LocateMenu(source);
            if (Inspect(target).IsInstalled) return new OperationResult {
                TargetPath = target, BeforeSha256 = before, AfterSha256 = before,
                FileVersionBefore = versionBefore, FileVersionAfter = versionBefore,
                Changed = false, OutsideMenuUnchanged = true, OfficialFileUnchanged = true,
                LocalizedPath = localized, Message = "ติดตั้งภาษาไทยชุดนี้ไว้แล้ว"
            };

            string html = LocalizeHtml(block.Html);
            byte[] payload = BuildPayload(html, block.Length);
            byte[] patched = (byte[])source.Clone();
            Buffer.BlockCopy(payload, 0, patched, block.Offset, payload.Length);
            AssertOutsideUnchanged(source, patched, block);
            string after = Hash(patched);
            string backupDirectory = NewBackupDirectory(target);
            string backupFile = System.IO.Path.Combine(backupDirectory, "ArcaneMPClient.dll");
            var record = new Dictionary<string, object> {
                { "sourceSha256", sourceHash }, { "localizedSha256", after },
                { "createdUtc", DateTime.UtcNow.ToString("o") }, { "installerVersion", "0.3.0" },
                { "hadModDllName", previousState == null ? config.ContainsKey("mod_dll_name") : previousState["hadModDllName"] },
                { "originalModDllName", previousState == null ? modName : previousState["originalModDllName"] }
            };
            config["mod_dll_name"] = ThaiDll;
            var changes = new List<FileChange>();
            if (before != sourceHash) changes.Add(new FileChange(target, source));
            changes.Add(new FileChange(localized, patched));
            changes.Add(new FileChange(System.IO.Path.Combine(directory, StateFile), JsonBytes(record)));
            changes.Add(new FileChange(System.IO.Path.Combine(directory, "ArcaneMP_launcher.json"), JsonBytes(config)));
            File.WriteAllBytes(backupFile, original);
            ApplyChanges(changes, backupDirectory);
            if (Hash(File.ReadAllBytes(target)) != sourceHash) throw new IOException("ตรวจ DLL ทางการหลังติดตั้งไม่ผ่าน");
            string versionAfter = VersionOf(target);
            if (!String.Equals(versionBefore, versionAfter, StringComparison.Ordinal))
                throw new IOException("ข้อมูลเวอร์ชันเปลี่ยนไป โปรดคืนค่าจากไฟล์สำรอง: " + backupFile);
            return new OperationResult {
                TargetPath = target, BackupPath = backupFile, BeforeSha256 = before, AfterSha256 = sourceHash,
                FileVersionBefore = versionBefore, FileVersionAfter = versionAfter,
                Changed = true, OutsideMenuUnchanged = true, OfficialFileUnchanged = before == sourceHash,
                LocalizedPath = localized,
                Message = "ติดตั้งภาษาไทยแยกจาก DLL ทางการแล้ว เปิด ArcaneMP ใหม่ได้"
            };
        }

        public static OperationResult Restore(string selected)
        {
            string target = ResolveTarget(selected);
            AssertLauncherClosed(target);
            AssertWritableAndClosed(target);
            byte[] current = ReadClient(target);
            string before = Hash(current);
            string directory = System.IO.Path.GetDirectoryName(target);
            var state = ReadState(directory);
            var config = ReadConfig(directory);
            if (state != null) {
                if (Value(config, "mod_dll_name") != ThaiDll)
                    throw new InvalidOperationException("การตั้งค่า Launcher ถูกเปลี่ยนหลังติดตั้ง จึงไม่เขียนทับการตั้งค่าปัจจุบัน");
                if (Convert.ToBoolean(state["hadModDllName"])) config["mod_dll_name"] = state["originalModDllName"];
                else config.Remove("mod_dll_name");
                string backupDirectory = NewBackupDirectory(target);
                string localized = System.IO.Path.Combine(directory, ThaiDll);
                var changes = new List<FileChange> {
                    new FileChange(System.IO.Path.Combine(directory, "ArcaneMP_launcher.json"), JsonBytes(config)),
                    new FileChange(System.IO.Path.Combine(directory, StateFile), null)
                };
                if (File.Exists(localized)) changes.Add(new FileChange(localized, null));
                ApplyChanges(changes, backupDirectory);
                return new OperationResult {
                    TargetPath = target, BackupPath = System.IO.Path.Combine(backupDirectory, "ArcaneMP_launcher.json"),
                    BeforeSha256 = before, AfterSha256 = Hash(File.ReadAllBytes(target)),
                    FileVersionBefore = VersionOf(target), FileVersionAfter = VersionOf(target),
                    Changed = true, OutsideMenuUnchanged = true, OfficialFileUnchanged = true,
                    Message = "คืนการตั้งค่า Launcher แล้ว โดยเก็บ DLL ทางการไว้เหมือนเดิม"
                };
            }
            Backup backup = FindBackup(target, before);
            if (backup == null) throw new InvalidOperationException("ไม่พบไฟล์สำรองที่ตรงกับไฟล์ปัจจุบัน จึงยังคืนค่าไม่ได้");
            byte[] original = File.ReadAllBytes(backup.Path);
            if (Hash(original) != backup.OriginalSha256) throw new InvalidOperationException("ไฟล์สำรองถูกเปลี่ยนแปลง จึงไม่สามารถนำมาใช้ได้");
            string retained = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(backup.Path), "before-restore-" + Guid.NewGuid().ToString("N") + ".dll");
            string versionBefore = VersionOf(target);
            ReplaceWithBackup(target, current, original, retained);
            return new OperationResult {
                TargetPath = target, BackupPath = retained, BeforeSha256 = before,
                AfterSha256 = Hash(original), FileVersionBefore = versionBefore, FileVersionAfter = VersionOf(target),
                Changed = true, OutsideMenuUnchanged = true, Message = "คืนไฟล์ก่อนติดตั้งแล้ว เปิด ArcaneMP ใหม่เพื่อใช้งาน"
            };
        }

        private sealed class FileChange
        {
            public string Path;
            public byte[] Before;
            public byte[] After;
            public FileChange(string path, byte[] after) { Path = path; After = after; }
        }

        private static string Value(Dictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? Convert.ToString(value) : "";
        }

        private static byte[] JsonBytes(object value) { return StrictUtf8.GetBytes(Json.Serialize(value)); }

        private static Dictionary<string, object> ReadConfig(string directory)
        {
            string path = System.IO.Path.Combine(directory, "ArcaneMP_launcher.json");
            if (!File.Exists(path)) return new Dictionary<string, object>();
            var config = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (config == null) throw new InvalidDataException("อ่านการตั้งค่า Launcher ไม่สำเร็จ");
            return config;
        }

        private static Dictionary<string, object> ReadState(string directory)
        {
            string path = System.IO.Path.Combine(directory, StateFile);
            if (!File.Exists(path)) return null;
            var state = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (state == null || !Regex.IsMatch(Value(state, "sourceSha256"), "^[a-f0-9]{64}$")
                || !Regex.IsMatch(Value(state, "localizedSha256"), "^[a-f0-9]{64}$")
                || !state.ContainsKey("hadModDllName") || !(state["hadModDllName"] is bool)
                || !state.ContainsKey("originalModDllName")
                || (Value(state, "originalModDllName") != "" && Value(state, "originalModDllName") != "ArcaneMPClient.dll"))
                throw new InvalidDataException("ข้อมูลการติดตั้งภาษาไทยไม่ถูกต้อง โปรดตรวจไฟล์สำรองก่อนทำต่อ");
            return state;
        }

        private static byte[] OfficialSource(string target, byte[] current)
        {
            string directory = System.IO.Path.GetDirectoryName(target);
            string manifest = System.IO.Path.Combine(directory, ".mafiahub", "build-manifest.json");
            string expected = "";
            if (File.Exists(manifest)) {
                var data = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest));
                object entries;
                if (data == null || !data.TryGetValue("files", out entries) || !(entries is System.Collections.IEnumerable))
                    throw new InvalidDataException("อ่านรายการไฟล์ของตัวอัปเดตไม่สำเร็จ โปรดเปิด Launcher ให้อัปเดตเสร็จก่อน");
                foreach (object entry in (System.Collections.IEnumerable)entries) {
                    var file = entry as Dictionary<string, object>;
                    if (Value(file, "path").Replace('\\', '/') == "ArcaneMPClient.dll") expected = Value(file, "sha256").ToLowerInvariant();
                }
                if (!Regex.IsMatch(expected, "^[a-f0-9]{64}$"))
                    throw new InvalidDataException("ไม่พบ checksum ของ DLL ทางการในรายการไฟล์อัปเดต");
            }
            string currentHash = Hash(current);
            MenuBlock currentBlock = LocateMenu(current);
            bool oldThai = currentBlock.Html.Contains(StartMarker) || currentBlock.Html.Contains("lang=\"th\"");
            if ((expected != "" && currentHash == expected) || (expected == "" && !oldThai)) return current;
            if (!oldThai) throw new InvalidOperationException("DLL ปัจจุบันไม่ตรงกับรายการอัปเดต โปรดเปิด Launcher ให้อัปเดตเสร็จก่อนติดตั้งภาษาไทย");

            byte[] source = null;
            string root = System.IO.Path.Combine(directory, "thai-menu-backups");
            if (expected != "" && Directory.Exists(root)) {
                foreach (string backupDirectory in Directory.GetDirectories(root).OrderByDescending(value => value)) {
                    string path = System.IO.Path.Combine(backupDirectory, "ArcaneMPClient.dll");
                    if (!File.Exists(path)) continue;
                    byte[] candidate = ReadClient(path);
                    if (Hash(candidate) == expected) { source = candidate; break; }
                }
            } else if (expected == "") {
                byte[] candidate = current;
                for (int depth = 0; depth < 12; depth++) {
                    Backup backup = FindBackup(target, Hash(candidate));
                    if (backup == null) break;
                    candidate = ReadClient(backup.Path);
                    string html = LocateMenu(candidate).Html;
                    if (!html.Contains(StartMarker) && !html.Contains("lang=\"th\"")) { source = candidate; break; }
                }
            }
            if (source == null) throw new InvalidOperationException("พบแพตช์ภาษาไทยแบบเก่า แต่ไม่พบ DLL ทางการที่ตรงกัน โปรดให้ Launcher อัปเดต/ซ่อมไฟล์ก่อน แล้วติดตั้งใหม่ด้วย EXE นี้");
            MenuBlock sourceBlock = LocateMenu(source);
            if (sourceBlock.Offset != currentBlock.Offset || sourceBlock.Length != currentBlock.Length)
                throw new InvalidDataException("ไฟล์สำรองมีโครงสร้างต่างจากไฟล์ปัจจุบัน จึงยังคืน DLL ทางการไม่ได้");
            AssertOutsideUnchanged(source, current, sourceBlock);
            return source;
        }

        private static bool Same(byte[] left, byte[] right)
        {
            return left == null ? right == null : right != null && Hash(left) == Hash(right);
        }

        private static byte[] ReadOptional(string path) { return File.Exists(path) ? File.ReadAllBytes(path) : null; }

        private static void ApplyChanges(List<FileChange> changes, string backupDirectory)
        {
            foreach (FileChange change in changes) {
                if (File.Exists(change.Path)) AssertWritableAndClosed(change.Path);
                change.Before = ReadOptional(change.Path);
                if (change.Before != null) File.WriteAllBytes(System.IO.Path.Combine(backupDirectory, System.IO.Path.GetFileName(change.Path)), change.Before);
            }
            var applied = new List<FileChange>();
            try {
                foreach (FileChange change in changes) {
                    if (!Same(ReadOptional(change.Path), change.Before)) throw new IOException("ไฟล์ถูกแก้ไขระหว่างทำงาน โปรดตรวจไฟล์อีกครั้ง");
                    WriteAtomic(change.Path, change.After);
                    applied.Add(change);
                    if (!Same(ReadOptional(change.Path), change.After)) throw new IOException("ตรวจไฟล์หลังบันทึกไม่ผ่าน");
                }
            } catch {
                applied.Reverse();
                foreach (FileChange change in applied) {
                    if (!Same(ReadOptional(change.Path), change.After)) throw new IOException("มีโปรแกรมอื่นแก้ไฟล์ระหว่างคืนค่า โปรดใช้ไฟล์สำรองใน " + backupDirectory);
                    WriteAtomic(change.Path, change.Before);
                }
                throw;
            }
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            if (bytes == null) { if (File.Exists(path)) File.Delete(path); return; }
            string stage = path + "." + Guid.NewGuid().ToString("N") + ".arcane-thai-stage";
            try {
                using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(stage, path, null, true);
                else File.Move(stage, path);
            } finally { if (File.Exists(stage)) File.Delete(stage); }
        }

        private static byte[] ReadClient(string target)
        {
            var info = new FileInfo(target);
            if (info.Length < 256 || info.Length > 512L * 1024 * 1024) throw new InvalidOperationException("ขนาดไฟล์ไม่ตรงกับไฟล์ไคลเอนต์ที่รองรับ");
            byte[] bytes = File.ReadAllBytes(target);
            if (bytes[0] != 'M' || bytes[1] != 'Z') throw new InvalidOperationException("ไฟล์ที่เลือกไม่ใช่โปรแกรม Windows ที่ถูกต้อง");
            int pe = BitConverter.ToInt32(bytes, 60);
            if (pe < 64 || pe > bytes.Length - 4 || bytes[pe] != 'P' || bytes[pe + 1] != 'E' || bytes[pe + 2] != 0 || bytes[pe + 3] != 0)
                throw new InvalidOperationException("โครงสร้างไฟล์โปรแกรมไม่ถูกต้อง");
            return bytes;
        }

        private static MenuBlock LocateMenu(byte[] bytes)
        {
            var candidates = new List<MenuBlock>();
            byte[] tag = Encoding.ASCII.GetBytes("<!doctype html>");
            for (int position = 0; position <= bytes.Length - tag.Length; position++)
            {
                if (bytes[position] != '<' || !MatchesAscii(bytes, position, tag)) continue;
                int terminator = Array.IndexOf(bytes, (byte)0, position);
                if (terminator < 0 || terminator - position > MaxHtmlBytes) continue;
                try {
                    string raw = StrictUtf8.GetString(bytes, position, terminator - position);
                    int closing = raw.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
                    if (closing < 0 || !String.IsNullOrWhiteSpace(raw.Substring(closing + 7))) continue;
                    string html = DecodeLoader(raw);
                    if (html.IndexOf("ArcaneMP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!html.Contains("menu:ready") && !html.Contains("settings:ready") && !html.Contains("window.arcanemp")) continue;
                    candidates.Add(new MenuBlock { Offset = position, Length = terminator - position, Html = html });
                } catch (DecoderFallbackException) { }
                  catch (InvalidDataException) { }
                  catch (FormatException) { }
            }
            if (candidates.Count == 0) throw new InvalidOperationException("ไม่พบหน้าเมนู ArcaneMP ที่ตัวติดตั้งนี้อ่านได้ ไฟล์เดิมจะไม่ถูกแก้ไข");
            if (candidates.Count != 1) throw new InvalidOperationException("พบหน้าเมนูมากกว่าหนึ่งตำแหน่ง จึงหยุดเพื่อรักษาไฟล์เดิม");
            return candidates[0];
        }

        private static bool MatchesAscii(byte[] bytes, int position, byte[] tag)
        {
            for (int index = 0; index < tag.Length; index++) {
                byte value = bytes[position + index];
                if (value >= 'A' && value <= 'Z') value = (byte)(value + 32);
                if (value != tag[index]) return false;
            }
            return true;
        }

        private static string DecodeLoader(string html)
        {
            for (int depth = 0; depth < 3 && html.Contains("DecompressionStream"); depth++) {
                if (html.Contains("menu:ready") || html.Contains("settings:ready")) break;
                Match match = Regex.Match(html, "atob\\(\"([A-Za-z0-9+/=]+)\"\\)");
                if (!match.Success) break;
                byte[] compressed = Convert.FromBase64String(match.Groups[1].Value);
                using (var input = new MemoryStream(compressed))
                using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream()) {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0) {
                        if (output.Length + read > MaxHtmlBytes) throw new InvalidDataException("HTML exceeds the supported size.");
                        output.Write(buffer, 0, read);
                    }
                    html = StrictUtf8.GetString(output.ToArray());
                }
            }
            return html;
        }

        private static string LocalizeHtml(string html)
        {
            html = Regex.Replace(html, Regex.Escape(StartMarker) + ".*?" + Regex.Escape(EndMarker), "", RegexOptions.Singleline);
            var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in ReadResourceText("ArcaneThai.Translations").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
                int tab = line.IndexOf('\t');
                if (tab <= 0) throw new InvalidDataException("Translation catalog is invalid.");
                dictionary[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
            dictionary["Version mismatch"] = "เวอร์ชันไม่ตรงกัน";
            dictionary["Kicked"] = "ถูกนำออกจากเซิร์ฟเวอร์";
            dictionary["Banned"] = "ถูกแบน";
            string script = ReadResourceText("ArcaneThai.Overlay").Replace("__ARCANE_THAI_DICTIONARY__", Json.Serialize(dictionary));
            string font = "data:font/woff2;base64," + Convert.ToBase64String(ReadResource("ArcaneThai.Font"));
            string face = "font-style:normal;font-weight:400 700;font-display:swap;src:url(" + font + ") format('woff2');unicode-range:U+0E00-0E7F,U+200B,U+25CC";
            string css = "@font-face{font-family:'Source Sans 3';" + face + "}@font-face{font-family:Marcellus;" + face + "}";
            css += "html:lang(th) :is(button,label,select,h2,h3,.tab,.list__title,.card__label,.stack__label,.menu__keys,.menu__title,.or){letter-spacing:normal;line-height:1.6}";
            css += "html:lang(th) :is(input,p,.field__help,.settings__lede){line-height:1.65}";
            string block = StartMarker + CatalogStamp() + "<style>" + css + "</style><script>" + script + "</script>" + EndMarker;
            int head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (head < 0) throw new InvalidOperationException("ไม่พบส่วนหัวของหน้าเมนู ไฟล์เดิมจะไม่ถูกแก้ไข");
            return html.Insert(head, block);
        }

        private static string CatalogStamp()
        {
            using (var data = new MemoryStream()) {
                foreach (string resource in new[] { "ArcaneThai.Translations", "ArcaneThai.Overlay", "ArcaneThai.Font" }) {
                    byte[] bytes = ReadResource(resource);
                    data.Write(bytes, 0, bytes.Length);
                }
                byte[] revision = Encoding.ASCII.GetBytes(Assembly.GetExecutingAssembly().GetName().Version.ToString());
                data.Write(revision, 0, revision.Length);
                return "<!-- arcane-thai-catalog:" + Hash(data.ToArray()) + " -->";
            }
        }

        private static byte[] BuildPayload(string html, int capacity)
        {
            byte[] compressed;
            using (var memory = new MemoryStream()) {
                using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, true)) {
                    byte[] utf8 = StrictUtf8.GetBytes(html);
                    gzip.Write(utf8, 0, utf8.Length);
                }
                compressed = memory.ToArray();
            }
            string loader = "<!DOCTYPE html><html lang=\"th\"><head><meta charset=\"utf-8\"><title>ArcaneMP</title></head><body><!-- ArcaneThaiLoader-v2 --><script>";
            loader += "(async()=>{const b=Uint8Array.from(atob(\"" + Convert.ToBase64String(compressed) + "\"),c=>c.charCodeAt(0));";
            loader += "const h=await new Response(new Blob([b]).stream().pipeThrough(new DecompressionStream(\"gzip\"))).text();document.open();document.write(h);document.close()})()";
            loader += ".catch(e=>{document.body.textContent=\"โหลดเมนูภาษาไทยไม่สำเร็จ: \"+e.message})</script></body></html>";
            byte[] data = StrictUtf8.GetBytes(loader);
            if (data.Length >= capacity) throw new InvalidOperationException("พื้นที่เมนูในไฟล์นี้ไม่เพียงพอสำหรับแพ็กภาษาไทย ไฟล์เดิมจะไม่ถูกแก้ไข");
            byte[] payload = new byte[capacity];
            for (int index = 0; index < capacity; index++) payload[index] = 32;
            Buffer.BlockCopy(data, 0, payload, 0, data.Length);
            return payload;
        }

        private static void AssertOutsideUnchanged(byte[] before, byte[] after, MenuBlock block)
        {
            if (before.Length != after.Length) throw new InvalidDataException("The file size changed.");
            for (int index = 0; index < before.Length; index++) {
                if (index >= block.Offset && index < block.Offset + block.Length) continue;
                if (before[index] != after[index]) throw new InvalidDataException("Bytes outside the menu were changed.");
            }
        }

        private static void AssertLauncherClosed(string target)
        {
            string launcher = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), "ArcaneMPLauncher.exe");
            foreach (Process process in Process.GetProcessesByName("ArcaneMPLauncher")) {
                using (process) {
                    string path;
                    try { path = process.MainModule.FileName; }
                    catch (System.ComponentModel.Win32Exception) { continue; }
                    catch (InvalidOperationException) { continue; }
                    if (String.Equals(System.IO.Path.GetFullPath(path), launcher, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Launcher ของตำแหน่งนี้ยังเปิดอยู่ โปรดปิด Hogwarts Legacy และ ArcaneMP ก่อนติดตั้งหรือคืนค่า");
                }
            }
        }

        private static void AssertWritableAndClosed(string target)
        {
            try { using (var stream = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
            catch (UnauthorizedAccessException) { throw new IOException("ไม่มีสิทธิ์เขียนไฟล์ในโฟลเดอร์ที่เลือก"); }
            catch (IOException) { throw new IOException("ไฟล์กำลังใช้งานอยู่ โปรดปิด Hogwarts Legacy และ ArcaneMP ก่อนติดตั้งหรือคืนค่า"); }
        }

        private static void ReplaceWithBackup(string target, byte[] expected, byte[] replacement, string backup)
        {
            string stage = target + "." + Guid.NewGuid().ToString("N") + ".arcane-thai-stage";
            try {
                if (Hash(File.ReadAllBytes(target)) != Hash(expected)) throw new IOException("ไฟล์ถูกแก้ไขระหว่างทำงาน โปรดตรวจไฟล์อีกครั้ง");
                using (var stream = new FileStream(stage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    stream.Write(replacement, 0, replacement.Length);
                    stream.Flush(true);
                }
                if (Hash(File.ReadAllBytes(stage)) != Hash(replacement)) throw new IOException("ตรวจไฟล์ที่เตรียมติดตั้งไม่ผ่าน");
                if (Hash(File.ReadAllBytes(target)) != Hash(expected)) throw new IOException("ไฟล์ถูกแก้ไขระหว่างทำงาน โปรดตรวจไฟล์อีกครั้ง");
                // File.Replace keeps the previous file as the backup in the same operation.
                File.Replace(stage, target, backup, true);
                if (Hash(File.ReadAllBytes(target)) != Hash(replacement)) throw new IOException("ตรวจไฟล์หลังติดตั้งไม่ผ่าน ไฟล์ก่อนติดตั้งอยู่ที่ " + backup);
            } finally {
                if (File.Exists(stage)) File.Delete(stage);
            }
        }

        private static string NewBackupDirectory(string target)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), "thai-menu-backups", DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static Backup FindBackup(string target, string currentHash)
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target), "thai-menu-backups");
            if (!Directory.Exists(root)) return null;
            foreach (string directory in Directory.GetDirectories(root).OrderByDescending(value => value)) {
                string original = System.IO.Path.Combine(directory, "ArcaneMPClient.dll");
                if (!File.Exists(original)) continue;
                foreach (string name in new[] { "installer-backup.json", "patch-manifest.json" }) {
                    string manifest = System.IO.Path.Combine(directory, name);
                    if (!File.Exists(manifest)) continue;
                    try {
                        var record = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest));
                        object patchedHash, originalHash;
                        if (record.TryGetValue("patchedSha256", out patchedHash) && record.TryGetValue("originalSha256", out originalHash)
                            && String.Equals(Convert.ToString(patchedHash), currentHash, StringComparison.OrdinalIgnoreCase)
                            && String.Equals(Hash(File.ReadAllBytes(original)), Convert.ToString(originalHash), StringComparison.OrdinalIgnoreCase))
                            return new Backup { Path = original, OriginalSha256 = Convert.ToString(originalHash), PatchedSha256 = currentHash };
                    } catch (IOException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
                }
            }
            return null;
        }

        internal static string ExtractForTests(string target) { return LocateMenu(ReadClient(target)).Html; }
        public static string FontLicenseText { get { return ReadResourceText("ArcaneThai.FontLicense"); } }
        public static string Serialize(object value) { return Json.Serialize(value); }
        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string VersionOf(string target) { return FileVersionInfo.GetVersionInfo(target).FileVersion ?? ""; }
        private static byte[] ReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if (stream == null) throw new InvalidDataException("Missing bundled resource: " + name);
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }
        private static string ReadResourceText(string name) { return StrictUtf8.GetString(ReadResource(name)); }
    }
}

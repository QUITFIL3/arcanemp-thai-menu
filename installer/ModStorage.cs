using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ArcaneThai
{
    internal static class ModStorage
    {
        public const string RelativeDirectory = @"mods\ArcaneThai";
        public const string MenuDll = @"mods\ArcaneThai\ArcaneMPClient.Thai.dll";
        // CEF resolves its subprocess/resources relative to the loaded module path.
        // Load the root hard link, while keeping the owned payload in mods.
        public const string MenuLoader = "ArcaneMPClient.Thai.dll";
        public static string DirectoryFor(string root) { return Path.Combine(root, RelativeDirectory); }

        public static void EnsureDirectory(string root, string directory)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string current = Path.GetFullPath(directory);
            if (!current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("ตำแหน่งมอดอยู่นอกโฟลเดอร์ ArcaneMP ที่เลือก");
            while (!String.Equals(current, root, StringComparison.OrdinalIgnoreCase)) {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("โฟลเดอร์มอดเป็นลิงก์ไปตำแหน่งอื่น โปรดเลือกโฟลเดอร์จริง");
                current = Path.GetDirectoryName(current);
            }
            Directory.CreateDirectory(directory);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdentity
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Created;
            public System.Runtime.InteropServices.ComTypes.FILETIME Accessed;
            public System.Runtime.InteropServices.ComTypes.FILETIME Written;
            public uint Volume;
            public uint SizeHigh;
            public uint SizeLow;
            public uint Links;
            public uint IndexHigh;
            public uint IndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileIdentity information);

        [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string newName, string existingName, IntPtr reserved);

        private static FileIdentity Identity(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                FileIdentity information;
                if (!GetFileInformationByHandle(stream.SafeFileHandle, out information)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return information;
            }
        }

        public static bool SameFile(string left, string right)
        {
            if (!File.Exists(left) || !File.Exists(right)) return false;
            FileIdentity a = Identity(left), b = Identity(right);
            return a.Volume == b.Volume && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow;
        }

        private static string NativePath(string path)
        {
            path = Path.GetFullPath(path);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }

        public static void Link(string alias, string source)
        {
            if (!CreateHardLink(NativePath(alias), NativePath(source), IntPtr.Zero))
                throw new IOException("สร้างลิงก์ให้ ArcaneMP โหลดจาก mods ไม่ได้ โปรดใช้โฟลเดอร์บน NTFS: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
    }
}

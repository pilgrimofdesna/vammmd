using System;
using System.Collections.Generic;
using MVR.FileManagementSecure;

namespace VamMmd
{
    // The user's dance folder: one sub-folder per song, each with one or more VMD files and
    // usually one WAV. Paths are kept with forward slashes, relative to the VaM folder.
    internal static class Library
    {
        public static string Norm(string p) { return p == null ? "" : p.Replace('\\', '/'); }

        public static string FileName(string p)
        {
            p = Norm(p);
            int i = p.LastIndexOf('/');
            return i < 0 ? p : p.Substring(i + 1);
        }

        public static string Folder(string p)
        {
            p = Norm(p);
            int i = p.LastIndexOf('/');
            return i < 0 ? "" : p.Substring(0, i);
        }

        public static bool HasExtension(string p, string ext)
        {
            return p != null && p.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }

        // Files with the extension in dir and up to depth levels below it, sorted by path.
        public static List<string> Files(string dir, string ext, int depth)
        {
            List<string> found = new List<string>();
            Collect(Norm(dir), ext, depth, found);
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        private static void Collect(string dir, string ext, int depth, List<string> found)
        {
            try
            {
                if (dir.Length == 0 || !FileManagerSecure.DirectoryExists(dir)) return;
                // all files, filtered here: VaM's own pattern match is case-sensitive and would miss "X.VMD"
                string[] files = FileManagerSecure.GetFiles(dir, null);
                if (files != null)
                    for (int i = 0; i < files.Length; i++)
                        if (HasExtension(files[i], ext)) found.Add(Norm(files[i]));
                if (depth <= 0) return;
                string[] dirs = FileManagerSecure.GetDirectories(dir);
                if (dirs != null)
                    for (int i = 0; i < dirs.Length; i++) Collect(Norm(dirs[i]), ext, depth - 1, found);
            }
            catch (Exception)
            {
                // a folder VaM will not read: treat as empty
            }
        }

        // Sub-folders of root that hold at least one VMD (looked for two levels deep), sorted by name.
        // root itself is listed first when VMD files lie directly in it.
        public static List<string> SongFolders(string root)
        {
            List<string> songs = new List<string>();
            root = Norm(root);
            try
            {
                if (root.Length == 0 || !FileManagerSecure.DirectoryExists(root)) return songs;
                if (Files(root, ".vmd", 0).Count > 0) songs.Add(root);
                string[] dirs = FileManagerSecure.GetDirectories(root);
                if (dirs == null) return songs;
                List<string> sorted = new List<string>();
                for (int i = 0; i < dirs.Length; i++) sorted.Add(Norm(dirs[i]));
                sorted.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < sorted.Count; i++)
                    if (Files(sorted[i], ".vmd", 2).Count > 0) songs.Add(sorted[i]);
            }
            catch (Exception)
            {
            }
            return songs;
        }
    }
}

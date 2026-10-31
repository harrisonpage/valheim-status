using System.IO;
using System.Text;

namespace ValheimStatus
{
    internal static class AtomicFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string TmpPath(string path) => path + ".tmp";

        public static void EnsureDir(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public static void CleanStaleTmp(string path)
        {
            string tmp = TmpPath(path);
            if (File.Exists(tmp)) File.Delete(tmp);
        }

        // The tmp file lives beside the target so the final rename is a same-filesystem
        // rename, i.e. readers (nginx) see either the old or the new file, never a
        // partial one. Mono's File.Move refuses to overwrite and File.Replace refuses a
        // missing destination, hence the branch.
        // Opened and closed on every call, so logrotate can rename the file away
        // and the next append starts a fresh one.
        public static void Append(string path, string contents) =>
            File.AppendAllText(path, contents, Utf8NoBom);

        public static void Write(string path, string contents)
        {
            string tmp = TmpPath(path);
            File.WriteAllText(tmp, contents, Utf8NoBom);
            if (File.Exists(path))
                File.Replace(tmp, path, null, true);
            else
                File.Move(tmp, path);
        }
    }
}

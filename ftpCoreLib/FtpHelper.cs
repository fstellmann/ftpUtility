namespace ftpCoreLib
{
    internal static class FtpHelper
    {
        /*
        internal static string CombineRemotePath(string remoteFolder, string fileName)
        {
            if (string.IsNullOrEmpty(remoteFolder) || remoteFolder == "/") return "/" + fileName;
            if (remoteFolder.EndsWith("/")) return remoteFolder + fileName;
            return remoteFolder + "/" + fileName;
        }
        */
        public static string CombineRemotePath(string folder, string relativePath)
        {
            relativePath = relativePath.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || folder == "/") return "/" + relativePath.TrimStart('/');
            if (!folder.EndsWith("/")) folder += "/";
            return folder + relativePath.TrimStart('/');
        }

        public static string GetRelativeRemotePath(string root, string fullPath)
        {
            root = string.IsNullOrEmpty(root) ? "/" : root;
            root = root.Replace('\\', '/');
            fullPath = fullPath.Replace('\\', '/');

            if (!root.EndsWith("/")) root += "/";
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                fullPath = fullPath.Substring(root.Length);

            return fullPath.TrimStart('/');
        }
    }
}

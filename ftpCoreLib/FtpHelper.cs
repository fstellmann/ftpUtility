namespace ftpCoreLib
{
    internal static class FtpHelper
    {
        internal static string CombineRemotePath(string remoteFolder, string fileName)
        {
            if (string.IsNullOrEmpty(remoteFolder) || remoteFolder == "/") return "/" + fileName;
            if (remoteFolder.EndsWith("/")) return remoteFolder + fileName;
            return remoteFolder + "/" + fileName;
        }
    }
}

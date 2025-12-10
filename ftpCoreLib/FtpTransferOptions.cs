namespace ftpCoreLib
{
    public enum FtpProtocol { Ftp, Ftps, Sftp }
    public enum FtpTransferMode { Upload, Download }
    public sealed class FtpTransferOptions
    {
        public FtpProtocol Protocol { get; set; } = FtpProtocol.Sftp;
        public FtpTransferMode Mode { get; set; } = FtpTransferMode.Download;

        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 0; // 0 = use default for protocol
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public bool UseKeyFile { get; set; } = false;
        public string KeyFilePath { get; set; } = string.Empty;

        public string RemoteFolder { get; set; } = "/";
        public string LocalFolder { get; set; } = ".";
        public bool DeleteSource { get; set; } = false;
        public bool OverwriteTarget { get; set; } = true;
        public bool Recursive { get; set; } = false;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Host)) throw new ArgumentException("Host must not be empty.", nameof(Host));
            if (string.IsNullOrWhiteSpace(Username)) throw new ArgumentException("Username must not be empty.", nameof(Username));
            if (Password == null) throw new ArgumentException("Password must not be null.", nameof(Password));
            if (string.IsNullOrWhiteSpace(RemoteFolder)) throw new ArgumentException("RemoteFolder must not be empty.", nameof(RemoteFolder));
            if (string.IsNullOrWhiteSpace(LocalFolder)) throw new ArgumentException("LocalFolder must not be empty.", nameof(LocalFolder));
        }
    }
}

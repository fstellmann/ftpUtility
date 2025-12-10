using System;
using System.Collections.Generic;
using System.Text;

namespace ftpUtility
{
    public sealed class Config
    {
        public string Protocol { get; set; } = "Sftp";
        public string Mode { get; set; } = "Download";
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public bool UseKeyFile { get; set; } = false;
        public string KeyFilePath { get; set; } = string.Empty;
        public string LocalFolder { get; set; } = ".";
        public string RemoteFolder { get; set; } = "/";
        public bool DeleteSource { get; set; }
        public bool OverwriteTarget { get; set; } = true;
        public bool PasswordEncrypted { get; set; } = true;
        public string PasswordFile { get; set; } = "pass.aes";
        public string? PlainPassword { get; set; }
        public bool Recursive { get; set; } = false;
    }
}

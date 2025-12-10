using ftpCoreLib;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using System.Text.Json;

namespace ftpUtility
{ 
    public static class Program
    {
        internal static readonly string generalPass = "myStrongKey123!";
        public static int Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddEventLog(new EventLogSettings
                {
                    SourceName = "FtpUtility",
                    LogName = "Application"
                });
                builder.SetMinimumLevel(LogLevel.Information);
            });
            var logger = loggerFactory.CreateLogger("FtpUtility.Cli");

            try
            {
                var configPath = Path.Combine(AppContext.BaseDirectory, "ftpconfig.json");
                if (!File.Exists(configPath))
                {
                    logger.LogError("Config file {Path} not found.", configPath);
                    return 1;
                }

                var json = File.ReadAllText(configPath);
                var model = JsonSerializer.Deserialize<Config>(json) ?? new Config();

                string password = model.PasswordEncrypted
                    ? DecryptPassword(model.PasswordFile)
                    : (model.PlainPassword ?? string.Empty);

                var options = new FtpTransferOptions
                {
                    Protocol = Enum.Parse<FtpProtocol>(model.Protocol, ignoreCase: true),
                    Mode = Enum.Parse<FtpTransferMode>(model.Mode, ignoreCase: true),
                    Host = model.Host,
                    Port = model.Port,
                    Username = model.Username,
                    Password = password,
                    LocalFolder = model.LocalFolder,
                    RemoteFolder = model.RemoteFolder,
                    DeleteSource = model.DeleteSource,
                    OverwriteTarget = model.OverwriteTarget
                };

                var service = new FtpTransferService();
                logger.LogInformation("Starting transfer: {Protocol} {Mode} {Host}:{Port}", options.Protocol, options.Mode, options.Host, options.Port);
                var result = service.Execute(options);

                if (!result.Success)
                {
                    logger.LogError(result.Exception, "Transfer failed after {Duration}.", result.Duration);
                    return 2;
                }

                logger.LogInformation("Transfer completed: {Count} files in {Duration}.", result.FileCount, result.Duration);
                return 0;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in FTP utility.");
                return 99;
            }
        }

        private static string DecryptPassword(string passwordFile)
        {
            string baseDir = AppContext.BaseDirectory;
            string encryptedPath = Path.Combine(baseDir, passwordFile);
            string tempPath = Path.Combine(baseDir, "pass.txt");

            Encryption.FileDecrypt(encryptedPath, tempPath, Program.generalPass);

            string password = File.ReadAllText(tempPath).Trim();
            File.Delete(tempPath);
            return password;
        }
    }
}
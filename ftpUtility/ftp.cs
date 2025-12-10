using FluentFTP;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ndFTP
{
    /* Extended Logging
      var serilogLogger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File("logs/FluentFTPLogs.txt", rollingInterval: RollingInterval.Day)
                    .CreateLogger();

                     var microsoftLogger = new SerilogLoggerFactory(serilogLogger)
                    .CreateLogger("FTP");
                     client.Logger = new FtpLogAdapter(microsoftLogger);
     */
    internal static class ftp
    {
        internal enum executionMode
        {
            download,
            upload
        }
        internal enum ftpMode
        {
            FTP,
            FTPS,
            SFTP
        }
        internal struct ftpValues
        {
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public executionMode mode { get; set; }
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public ftpMode security { get; set; }
            public string host { get; set; }
            public int port { get; set; }
            public string user { get; set; }
            public string password { get; set; }
            public string remoteFolder { get; set; }
            public string localFolder { get; set; }
            public bool deleteSource { get; set; }
            public bool overwriteTarget { get; set; }
            public int maxParallelConnections { get; set; }
        }

        public static ftpValues val = new ftpValues();
        public static int fileCount = 0;
        public static void setFTPValues()
        {
            try
            {
                string configPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), @"config.json");
                if(String.IsNullOrEmpty(configPath))
                {
                  //  Log.Fatal("Keine config-Datei gefunden.");
                }
                val = JsonSerializer.Deserialize<ftpValues>(File.ReadAllText(configPath));
                val.password = Program.passwordManager();
                if (!Directory.Exists(val.localFolder))
                {
                    Directory.CreateDirectory(val.localFolder);
                }
            }
            catch (Exception exc)
            {
               // Log.Error(exc.Message);
            }
        }
        public static bool checkConnection() 
        {
            if(val.security == ftpMode.SFTP) { return sftp.checkConnection(); }

            FtpClient client = new FtpClient(val.host, val.user, val.password, val.port);
            try
            {
                //client.Config.ConnectTimeout = TimeSpan.FromSeconds(10).Seconds;
                //client.Config.LogToConsole = true;
              //  Log.Information("Checking ftp-Connection...");
                var hold = client.AutoDetect();
                client.Config.ValidateAnyCertificate = true;
                
                client.AutoConnect();
                Console.WriteLine(client.ConnectionType);
            }
            catch(Exception exc)
            {
             //   Log.Error(exc.Message);
                return false;
            }
            return true; 
        }

        #region download
        public static bool checkForFiles()
        {
            using (FtpClient client = new FtpClient(val.host, val.user, val.password, val.port))
            {
                try
                {
                    client.AutoConnect();

                    var files = client.GetListing(val.remoteFolder);

                    if (files.Where(x => x.Type == FtpObjectType.File).Count() > 0)
                    {
                        Log.Information($"Es sind {files.Where(x => x.Type == FtpObjectType.File).Count()} Dateien auf dem ftp-Server vorhanden.");
                        client.Disconnect();
                        return true;
                    }

                    client.Disconnect();
                }
                catch (Exception exc)
                {
                    Log.Error(exc.Message);
                }
            }
            return false;
        }
        public static bool downloadFilesParallel()
        {
            try
            {
                var clients = new ConcurrentBag<FtpClient>();
                var opts = new ParallelOptions { MaxDegreeOfParallelism = ftp.val.maxParallelConnections };

                FtpClient clientList = new FtpClient(val.host, val.user, val.password, val.port);

                clientList.AutoConnect();

                var files = clientList.GetListing(val.remoteFolder);
                clientList.Disconnect();
                Parallel.ForEach(files, opts, file =>
                {
                    string thread = $"Thread {Thread.CurrentThread.ManagedThreadId}";
                    if (!clients.TryTake(out var client))
                    {
                        client = new FtpClient(val.host, val.user, val.password, val.port);
                        client.AutoConnect();
                    }

                    string tempFileName = Path.Combine(val.localFolder, Path.GetFileName(file.FullName));
                    string desc = $"{thread}, Connection {client.GetHashCode()}, " + $"File {file.FullName} => {tempFileName}";
                    Log.Information(desc);

                    client.DownloadFile(tempFileName, file.FullName, existsMode: (val.overwriteTarget ? FtpLocalExists.Overwrite : FtpLocalExists.Skip));

                    fileCount++;
                    clients.Add(client);
                });

                foreach (var pClient in clients)
                {
                    pClient.Dispose();
                }
            }
            catch (Exception exc)
            {
                Log.Error(exc.Message);
            }
            return true;
        }
        public static void deleteFilesRemote()
        {
            using (FtpClient client = new FtpClient(val.host, val.user, val.password, val.port))
            {
                try
                {
                    client.AutoConnect();

                    var files = client.GetListing(val.remoteFolder);

                    Parallel.ForEach(files, f =>
                    {
                        if (f.Type == FtpObjectType.File)
                        {
                            Log.Information("Lösche Datei von ftp: " + f.FullName);
                            client.DeleteFile(f.FullName);
                        }
                    });
                    client.Disconnect();
                }
                catch (Exception exc)
                {
                    Log.Error(exc.Message);
                }
            }
        }
        #endregion
        #region upload
        public static bool checkForFilesLocal()
        {
            try
            {
                var files = Directory.GetFiles(val.localFolder);

                if (files.Count() > 0)
                {
                    Log.Information($"Es sind {files.Count()} Dateien vorhanden.");
                    return true;
                }
            }
            catch (Exception exc)
            {
                Log.Error(exc.Message);
            }
            Log.Information($"Es sind keine neuen Dateien vorhanden.");
            return false;
        }
        public static bool uploadFilesParallel()
        {
            try
            {
                var clients = new ConcurrentBag<FtpClient>();
                var opts = new ParallelOptions { MaxDegreeOfParallelism = ftp.val.maxParallelConnections };

                var files = Directory.GetFiles(val.localFolder);

                Parallel.ForEach(files, opts, file =>
                {
                    string thread = $"Thread {Thread.CurrentThread.ManagedThreadId}";
                    if (!clients.TryTake(out var client))
                    {
                        client = new FtpClient(val.host, val.user, val.password, val.port);
                        client.Config.ValidateAnyCertificate = true;
                        client.AutoConnect();
                    }

                    string tempFileName = Path.Combine(val.remoteFolder, Path.GetFileName(file));
                    string desc = $"{thread}, Connection {client.GetHashCode()}, " + $"File {file} => {tempFileName}";
                    Log.Information(desc);

                    client.UploadFile(file, tempFileName, existsMode: (val.overwriteTarget ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip));

                    fileCount++;
                    clients.Add(client);
                });

                foreach (var pClient in clients)
                {
                    pClient.Dispose();
                }
            }
            catch (Exception exc)
            {
                Log.Error(exc.Message);
            }
            return true;
        }
        public static void deleteFilesLocal()
        {
            try
            {
                Parallel.ForEach(Directory.GetFiles(val.localFolder), f =>
                {
                    Log.Information("Lösche Datei: " + f);
                    File.Delete(f);
                });
            }
            catch (Exception exc)
            {
                Log.Error(exc.Message);
            }
        }
        #endregion
    }
}
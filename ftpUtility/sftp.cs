using FluentFTP;
using FluentFTP.Logging;
using Renci.SshNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ndFTP
{
    internal static class sftp
    {
        public static bool checkConnection()
        {
          //  Log.Information("Checking sftp-Connection...");
            var client = new SftpClient(ftp.val.host, ftp.val.user, ftp.val.password);
            client.Connect();
            //Log.Information("MaxSessions: "+client.ConnectionInfo.MaxSessions.ToString());
            //Log.Information("IsAuthenticated: "+client.ConnectionInfo.IsAuthenticated.ToString());
            client.Disconnect();
            return true;
        }
        #region upload
        public static bool uploadFilesParallel()
        {
            try
            {
                var clients = new ConcurrentBag<SftpClient>();
                var opts = new ParallelOptions { MaxDegreeOfParallelism = ftp.val.maxParallelConnections };
                var exceptions = new ConcurrentQueue<Exception>();
                var files = Directory.GetFiles(ftp.val.localFolder);

                Parallel.ForEach(files, opts, file =>
                {
                        string thread = $"Thread {Thread.CurrentThread.ManagedThreadId}";
                        if (!clients.TryTake(out var client))
                        {
                            client = new SftpClient(ftp.val.host, ftp.val.user, ftp.val.password);
                            client.Connect();
                        }

                        string tempFileName = $"{ftp.val.remoteFolder}{Path.GetFileName(file)}";
                        string desc = $"{thread}, Connection {client.GetHashCode()}, " + $"File {file} => {tempFileName}";
                        Log.Information(desc);
                        using (FileStream fs = new FileStream(file, FileMode.Open))
                        {
                            client.UploadFile(fs, tempFileName, ftp.val.overwriteTarget);
                            fs.Close();
                        }

                        ftp.fileCount++;
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
        #endregion

        #region download

        public static void deleteFilesRemote()
        {
            using (SftpClient client = new SftpClient(ftp.val.host, ftp.val.user, ftp.val.password))
            {
                try
                {
                    client.Connect();
                    var files = client.ListDirectory(ftp.val.remoteFolder);
                    Parallel.ForEach(files, f =>
                    {
                        if (!f.IsDirectory)
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
        public static bool checkForFiles()
        {
            using (SftpClient client = new SftpClient(ftp.val.host, ftp.val.user, ftp.val.password))
            {
                try
                {
                    client.Connect();
                    var files = client.ListDirectory(ftp.val.remoteFolder);

                    if (files.Where(x => x.IsDirectory == false).Count() > 0)
                    {
                        Log.Information($"Es sind {files.Where(x => x.IsDirectory == false).Count()} Dateien auf dem ftp-Server vorhanden.");
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
                var clients = new ConcurrentBag<SftpClient>();
                var opts = new ParallelOptions { MaxDegreeOfParallelism = ftp.val.maxParallelConnections };

                var clientListing = new SftpClient(ftp.val.host, ftp.val.user, ftp.val.password);
                clientListing.Connect();
                var files = clientListing.ListDirectory(ftp.val.remoteFolder);
                clientListing.Disconnect();

                Parallel.ForEach(files.Where(x => x.IsDirectory == false), opts, file =>
                {
                    string thread = $"Thread {Thread.CurrentThread.ManagedThreadId}";
                    if (!clients.TryTake(out var client))
                    {
                        client = new SftpClient(ftp.val.host, ftp.val.user, ftp.val.password);
                        client.Connect();
                    }

                    string tempFileName = Path.Combine(ftp.val.localFolder, file.Name);
                    string desc = $"{thread}, Connection {client.GetHashCode()}, " + $"File {file.FullName} => {tempFileName}";
                    Log.Information(desc);
                    if (ftp.val.overwriteTarget && File.Exists(tempFileName))
                    {
                        File.Delete(tempFileName);
                    }
                    using (FileStream fs = new FileStream(tempFileName, FileMode.CreateNew))
                    {
                        client.DownloadFile(file.FullName, fs);
                        fs.Close();
                    }

                    ftp.fileCount++;
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
        #endregion
    }
}

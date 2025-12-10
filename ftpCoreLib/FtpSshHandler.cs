using Renci.SshNet;
using Renci.SshNet.Sftp;
using System.Collections.Concurrent;

namespace ftpCoreLib
{
    internal class FtpSshHandler : IDisposable
    {
        private bool _disposed;
        private FtpTransferOptions options { get; set; }
        public FtpSshHandler(FtpTransferOptions _options)
        {
            options = _options;
        }
        internal int ExecuteSftp(CancellationToken cancellationToken)
        {
            return options.Mode == FtpTransferMode.Download ? options.UseKeyFile ? DownloadSsh(cancellationToken) : DownloadSftp(cancellationToken) : options.UseKeyFile ? UploadSsh(cancellationToken) : UploadSftp(cancellationToken);
        }

        private SftpClient CreateSftpClient()
        {
            int port = options.Port > 0 ? options.Port : 22;
            return new SftpClient(options.Host, port, options.Username, options.Password);
        }

        private SftpClient CreateSshClient()
        {
            int port = options.Port > 0 ? options.Port : 22;

            var keyBytes = File.ReadAllBytes(options.KeyFilePath);
            using (var keyStream = new MemoryStream(keyBytes, false))
            {
                PrivateKeyFile pkFile = string.IsNullOrEmpty(options.Password) ? new PrivateKeyFile(keyStream) : new PrivateKeyFile(keyStream, options.Password);
                var connectionInfo = new PrivateKeyConnectionInfo(options.Host, port, options.Username, pkFile);

                return new SftpClient(connectionInfo);
            }
        }

        private int DownloadSsh(CancellationToken cancellationToken)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            using var initialClient = CreateSshClient();
            initialClient.Connect();

            // var listing = initialClient.ListDirectory(options.RemoteFolder).Where(x => !x.IsDirectory && !x.IsSymbolicLink).ToArray();
            var listing = options.Recursive ? GetRecursiveRemoteFiles(initialClient, options.RemoteFolder, cancellationToken).ToArray() : initialClient.ListDirectory(options.RemoteFolder).Where(x => !x.IsDirectory && !x.IsSymbolicLink).ToArray();
            if (listing.Length == 0) return 0;

            var clients = new ConcurrentBag<SftpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(listing, parallelOptions, file =>
            {
                SftpClient client;
                if (!clients.TryTake(out client!))
                {
                    client = CreateSshClient();
                    client.Connect();
                }

                try
                {
                    string relative = FtpHelper.GetRelativeRemotePath(options.RemoteFolder, file.FullName);
                    string localPath = Path.Combine(options.LocalFolder, relative.Replace('/', Path.DirectorySeparatorChar));
                    string? dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    if (File.Exists(localPath))
                    {
                        if (!options.OverwriteTarget) return;
                        File.Delete(localPath);
                    }

                    using var fs = File.Open(localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    client.DownloadFile(file.FullName, fs);
                    Interlocked.Increment(ref fileCount);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
                finally
                {
                    clients.Add(client);
                }
            });

            while (clients.TryTake(out var c))
            {
                if (c.IsConnected) c.Disconnect();
                c.Dispose();
            }

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);

            if (options.DeleteSource)
            {
                using var client = CreateSshClient();
                client.Connect();
                foreach (var file in listing)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    client.DeleteFile(file.FullName);
                }
                client.Disconnect();
            }

            return fileCount;
        }

        private int UploadSsh(CancellationToken cancellationToken)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            // var files = Directory.GetFiles(options.LocalFolder);
            var files = Directory.GetFiles(options.LocalFolder, "*", options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

            if (files.Length == 0) return 0;

            using var initialClient = CreateSshClient();
            initialClient.Connect();

            var clients = new ConcurrentBag<SftpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(files, parallelOptions, filePath =>
            {
                SftpClient client;
                if (!clients.TryTake(out client!))
                {
                    client = CreateSshClient();
                    client.Connect();
                }

                try
                {
                    string relative = Path.GetRelativePath(options.LocalFolder, filePath).Replace(Path.DirectorySeparatorChar, '/');
                    string remotePath = FtpHelper.CombineRemotePath(options.RemoteFolder, relative);

                    string remoteDir = remotePath.Contains('/') ? remotePath[..remotePath.LastIndexOf('/')] : options.RemoteFolder;

                    EnsureRemoteDirectoryExists(client, remoteDir);

                    using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (client.Exists(remotePath))
                    {
                        if (!options.OverwriteTarget) return;
                        client.DeleteFile(remotePath);
                    }

                    client.UploadFile(fs, remotePath);
                    Interlocked.Increment(ref fileCount);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
                finally
                {
                    clients.Add(client);
                }
            });

            while (clients.TryTake(out var c))
            {
                if (c.IsConnected) c.Disconnect();
                c.Dispose();
            }

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);

            if (options.DeleteSource)
            {
                foreach (var filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(filePath);
                }
            }

            return fileCount;
        }

        private int DownloadSftp(CancellationToken cancellationToken)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            using var initialClient = CreateSftpClient();
            initialClient.Connect();

            //   var listing = initialClient.ListDirectory(options.RemoteFolder).Where(x => !x.IsDirectory && !x.IsSymbolicLink).ToArray();
            var listing = options.Recursive ? GetRecursiveRemoteFiles(initialClient, options.RemoteFolder, cancellationToken).ToArray() : initialClient.ListDirectory(options.RemoteFolder).Where(x => !x.IsDirectory && !x.IsSymbolicLink).ToArray();

            if (listing.Length == 0) return 0;

            var clients = new ConcurrentBag<SftpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(listing, parallelOptions, file =>
            {
                SftpClient client;
                if (!clients.TryTake(out client!))
                {
                    client = CreateSftpClient();
                    client.Connect();
                }

                try
                {
                    string relative = FtpHelper.GetRelativeRemotePath(options.RemoteFolder, file.FullName);
                    string localPath = Path.Combine(options.LocalFolder, relative.Replace('/', Path.DirectorySeparatorChar));
                    string? dir = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    if (File.Exists(localPath))
                    {
                        if (!options.OverwriteTarget) return;
                        File.Delete(localPath);
                    }

                    using var fs = File.Open(localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    client.DownloadFile(file.FullName, fs);
                    Interlocked.Increment(ref fileCount);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
                finally
                {
                    clients.Add(client);
                }
            });

            while (clients.TryTake(out var c))
            {
                if (c.IsConnected) c.Disconnect();
                c.Dispose();
            }

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);

            if (options.DeleteSource)
            {
                using var client = CreateSftpClient();
                client.Connect();
                foreach (var file in listing)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    client.DeleteFile(file.FullName);
                }
                client.Disconnect();
            }

            return fileCount;
        }

        private int UploadSftp(CancellationToken cancellationToken)
        {
            int fileCount = 0;
            var exceptions = new ConcurrentQueue<Exception>();

            //   var files = Directory.GetFiles(options.LocalFolder);
            var files = Directory.GetFiles(options.LocalFolder, "*", options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return 0;

            using var initialClient = CreateSftpClient();
            initialClient.Connect();

            var clients = new ConcurrentBag<SftpClient>();
            clients.Add(initialClient);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            Parallel.ForEach(files, parallelOptions, filePath =>
            {
                SftpClient client;
                if (!clients.TryTake(out client!))
                {
                    client = CreateSftpClient();
                    client.Connect();
                }

                try
                {
                    string relative = Path.GetRelativePath(options.LocalFolder, filePath).Replace(Path.DirectorySeparatorChar, '/');
                    string remotePath = FtpHelper.CombineRemotePath(options.RemoteFolder, relative);

                    string remoteDir = remotePath.Contains('/') ? remotePath[..remotePath.LastIndexOf('/')] : options.RemoteFolder;

                    EnsureRemoteDirectoryExists(client, remoteDir);

                    using var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (client.Exists(remotePath))
                    {
                        if (!options.OverwriteTarget) return;
                        client.DeleteFile(remotePath);
                    }

                    client.UploadFile(fs, remotePath);
                    Interlocked.Increment(ref fileCount);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
                finally
                {
                    clients.Add(client);
                }
            });

            while (clients.TryTake(out var c))
            {
                if (c.IsConnected) c.Disconnect();
                c.Dispose();
            }

            if (!exceptions.IsEmpty) throw new AggregateException(exceptions);

            if (options.DeleteSource)
            {
                foreach (var filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(filePath);
                }
            }

            return fileCount;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (!disposing) return;
        }

        private IEnumerable<ISftpFile> GetRecursiveRemoteFiles(SftpClient client, string root, CancellationToken token)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string current = stack.Pop();

                foreach (var entry in client.ListDirectory(current))
                {
                    if (entry.IsSymbolicLink) continue;

                    if (entry.IsDirectory)
                    {
                        if (entry.Name == "." || entry.Name == "..") continue;
                        stack.Push(entry.FullName);
                    }
                    else
                    {
                        yield return entry;
                    }
                }
            }
        }

        private void EnsureRemoteDirectoryExists(SftpClient client, string remoteDir)
        {
            if (string.IsNullOrWhiteSpace(remoteDir)) return;

            remoteDir = remoteDir.Replace('\\', '/');
            if (remoteDir == "/") return;

            string[] parts = remoteDir.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            string current = "/";

            foreach (var part in parts)
            {
                current = current == "/" ? "/" + part : current + "/" + part;
                if (!client.Exists(current))
                {
                    client.CreateDirectory(current);
                }
            }
        }
    }
}

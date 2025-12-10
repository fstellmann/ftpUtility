
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace ndFTP
{
    internal class Program
    {
        private static string passwordPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "pass.txt");
        private static readonly protected string generalPass = @"ap!m6jP?TQN!zJE?";
       // public static readonly int maxConnections = 32;
        public static bool encrypt { get; set; }

        [DllImport("KERNEL32.DLL", EntryPoint = "RtlZeroMemory")]
        public static extern bool ZeroMemory(IntPtr Destination, int Length);
        static void Main(string[] args)
        {
            #region setup
           // SelfLog.Enable(msg => Debug.WriteLine(msg));
            initializeGeneralLogger(toEmail: "f.stellmann@nasdo.de");

           // Parser.Default.ParseArguments<CommandLineArguments>(args).WithParsed(RunOptions).WithNotParsed(HandleParseError);

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
            #endregion
            if (encrypt)
            {
                string path = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "passwort.txt");
                Encryption.FileEncrypt(path, generalPass);
                string passwordFilePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "pass.aes");
                if (File.Exists(passwordFilePath))
                {
                    File.Delete(passwordFilePath);
                }
                File.Move(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), Path.GetFileNameWithoutExtension(path) + ".aes"), passwordFilePath);
                Environment.Exit(0);
            }
            else
            {
                ftp.setFTPValues();
               // Log.Information($"[{ftp.val.security}] - Starting {ftp.val.mode} {(ftp.val.mode == ftp.executionMode.upload ? "to":"from")} {ftp.val.host}:{ftp.val.port}");
                if (ftp.checkConnection())
                {
                    if (ftp.val.mode == ftp.executionMode.download)
                    {
                        if(ftp.val.security == ftp.ftpMode.SFTP)
                        {
                            if (sftp.checkForFiles())
                            {
                                try
                                {
                                    Stopwatch sw = new Stopwatch();
                                    sw.Start();
                                    sftp.downloadFilesParallel();
                                    if (ftp.val.deleteSource) sftp.deleteFilesRemote();
                                    sw.Stop();
                                   // Log.Information($"ftp-Download in {TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds).TotalSeconds.ToString("f4")}s abgeschlossen. {ftp.fileCount} Dateien heruntergeladen.");
                                }
                                catch (Exception exc)
                                {
                                    string msg = String.Format("{0} || Meldung: {1}\r\nStacktrace:\r\n{2}" + Environment.NewLine, DateTime.Now, exc.Message, exc.StackTrace);
                                  //  Log.Error(msg);
                                }
                            }
                            else
                            {
                               // Log.Information("Keine neuen Dateien auf ftp-Server gefunden. Breche ab...");
                            }
                        }
                        if(ftp.val.security == ftp.ftpMode.FTP || ftp.val.security == ftp.ftpMode.FTPS)
                        {
                            if (ftp.checkForFiles())
                            {
                                try
                                {
                                    Stopwatch sw = new Stopwatch();
                                    sw.Start();
                                    ftp.downloadFilesParallel();
                                    if (ftp.val.deleteSource) ftp.deleteFilesRemote();
                                    sw.Stop();
                                   // Log.Information($"ftp-Download in {TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds).TotalSeconds.ToString("f4")}s abgeschlossen. {ftp.fileCount} Dateien heruntergeladen.");
                                }
                                catch (Exception exc)
                                {
                                    string msg = String.Format("{0} || Meldung: {1}\r\nStacktrace:\r\n{2}" + Environment.NewLine, DateTime.Now, exc.Message, exc.StackTrace);
                                   // Log.Error(msg);
                                }
                            }
                            else
                            {
                               // Log.Information("Keine neuen Dateien auf ftp-Server gefunden. Breche ab...");
                            }
                        }
                    }
                    if (ftp.val.mode == ftp.executionMode.upload)
                    {
                        if (ftp.checkForFilesLocal())
                        {
                            try
                            {
                                Stopwatch sw = new Stopwatch();
                                sw.Start();
                                if (ftp.val.security == ftp.ftpMode.SFTP) { sftp.uploadFilesParallel(); }
                                if (ftp.val.security == ftp.ftpMode.FTP || ftp.val.security == ftp.ftpMode.FTPS) { ftp.uploadFilesParallel(); }

                                if (ftp.val.deleteSource) ftp.deleteFilesLocal();
                                sw.Stop();
                              //  Log.Information($"ftp-Upload in {TimeSpan.FromMilliseconds(sw.ElapsedMilliseconds).TotalSeconds.ToString("f4")}s abgeschlossen. {ftp.fileCount} Dateien hochgeladen.");
                            }
                            catch (Exception exc)
                            {
                                string msg = String.Format("{0} || Meldung: {1}\r\nStacktrace:\r\n{2}" + Environment.NewLine, DateTime.Now, exc.Message, exc.StackTrace);
                               // Log.Error(msg);
                            }
                        }
                        else
                        {
                          //  Log.Information("Keine neuen Dateien auf Filesystem gefunden. Breche ab...");
                        }
                    }
                }
                else
                {
                   // Log.Error("ftp-Server ist nicht erreichbar oder die Verbindung konnte nicht hergestellt werden. Breche ab...");
                    Environment.Exit(0);
                }
            }
        }

        private static void archiveFiles()
        {
            if (!Directory.Exists("./archive"))
            {
                Directory.CreateDirectory("./archive");
            }
            foreach (string s in Directory.GetFiles(ftp.val.localFolder))
            {
                string tempFileName = Path.Combine("./archive", Path.GetFileName(s));
                File.Copy(s, tempFileName);
            }
        }

        public static string passwordManager()
        {
            string localPasswordPath = Path.Combine(Path.GetDirectoryName(passwordPath), Path.GetFileNameWithoutExtension(passwordPath) + ".aes");
            if (!File.Exists(localPasswordPath))
            {
               // Log.Fatal("Kein Passwort verfügbar");
                Environment.Exit(0);
            }
            GCHandle gch = GCHandle.Alloc(generalPass, GCHandleType.Pinned);
            Encryption.FileDecrypt(localPasswordPath, passwordPath, generalPass);
            ZeroMemory(gch.AddrOfPinnedObject(), generalPass.Length * 2);
            gch.Free();
            string ret = File.ReadAllText(passwordPath);
            File.Delete(passwordPath);
            return ret;
        }
        #region setup
        public static void initializeGeneralLogger(string toEmail)
        {
            /*
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: @"[{ProcessId}:{ThreadId}] {Timestamp:dd-MM-yyyy HH:mm:ss.fff} || {Level} [{EnvironmentUserName}@{MachineName}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File($"logs\\{typeof(Program).Assembly.GetName().Name}.log"
            , rollingInterval: RollingInterval.Day
            , outputTemplate: @"[{ProcessId}:{ThreadId}] {Timestamp:dd-MM-yyyy HH:mm:ss.fff} || {Level} [{EnvironmentUserName}@{MachineName}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.EventLog(source: "Application")
            .WriteTo.Email(new EmailConnectionInfo
            {
                FromEmail = "f.stellmann@nasdo.de",
                ToEmail = toEmail,
                MailServer = "smtp.office365.com",
                NetworkCredentials = new NetworkCredential
                {
                    UserName = "f.stellmann@nasdo.de",
                    Password = "MSj@dJjTeA!9g4LB"
                },
                EnableSsl = false,
                Port = 587,
                ServerCertificateValidationCallback = delegate { return true; },
                EmailSubject = $"Fehler in {typeof(Program).Assembly.GetName().Name}.exe"
            }
             , restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error
             , outputTemplate: @"[{ProcessId}:{ThreadId}] {Timestamp:dd-MM-yyyy HH:mm:ss.fff} || {Level} [{EnvironmentUserName}@{MachineName}] {Message:lj}{NewLine}{Exception}")
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentUserName()
            .CreateLogger();
            */
        }
        static void RunOptions(CommandLineArguments cla)
        {
            encrypt = cla.encrypt;
        }
       

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            string msg = String.Format("{0} || Meldung: {1}\r\nStacktrace:\r\n{2}" + Environment.NewLine, DateTime.Now, (e.ExceptionObject as Exception).Message, (e.ExceptionObject as Exception).StackTrace);
           // Log.Fatal(msg);
        }
        #endregion
    }
}
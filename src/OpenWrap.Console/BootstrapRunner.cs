using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using OpenWrap.Console.TinySharpZip;

namespace OpenWrap.Console
{
    public class BootstrapRunner
    {
        readonly string _bootstrapAddress;
        readonly string _cachePath;
        readonly string _entryPointPackage;
        readonly INotifier _notifier;
        readonly Regex _openwrapRegex;
        readonly string _rootPath;
        readonly string _wrapsPath;
        FileInfo _currentExecutable;

        public BootstrapRunner(string rootPath, string cachePath, string wrapsPath, IEnumerable<string> packageNamesToLoad, string bootstrapAddress, INotifier notifier)
        {
            _cachePath = cachePath;
            _wrapsPath = wrapsPath;
            _rootPath = rootPath;
            _notifier = notifier;
            _entryPointPackage = packageNamesToLoad.First();
            _openwrapRegex = new Regex(string.Format(@"^(?<name>{0})-(?<version>\d+(\.\d+(\.\d+(\.\d+)?)?)?)$", string.Join("|", packageNamesToLoad.ToArray())), RegexOptions.IgnoreCase);
            _bootstrapAddress = bootstrapAddress;
        }

        public BootstrapResult Run(string[] args)
        {
            if (args.Contains("-debug", StringComparer.OrdinalIgnoreCase))
            {
                Debugger.Launch();
                args = args.Where(x => x.IndexOf("-debug", StringComparison.OrdinalIgnoreCase) == -1).ToArray();
            }
            try
            {
                _currentExecutable = new FileInfo(Assembly.GetEntryAssembly().Location);
                VerifyConsoleInstalled();
            }
            catch (Exception e)
            {
                return _notifier.BootstrappingFailed(e);
            }
            try
            {
                var bootstrapAssemblies = GetAssemblyPathsForProjectRepository();
                if (bootstrapAssemblies.Count() == 0)
                    bootstrapAssemblies = GetAssemblyPathsForSystemRepository();
                if (bootstrapAssemblies.Count() == 0)
                    bootstrapAssemblies = TryDownloadOpenWrap();

                if (bootstrapAssemblies.Count() == 0)
                    throw new EntryPointNotFoundException("Could not find OpenWrap assemblies in either current project or system repository.");

                var assemblyFiles = from asm in bootstrapAssemblies
                                    let pathNet40 = Path.Combine(asm, "bin-net40")
                                    let pathNet35 = Path.Combine(asm, "bin-net35")
                                    let net40Binaries = Directory.Exists(pathNet40) ? Directory.GetFiles(pathNet40, "*.dll") : new string[0]
                                    let net35Binaries = Directory.Exists(pathNet35) ? Directory.GetFiles(pathNet35, "*.dll") : new string[0]
                                    from file in net40Binaries.Concat(net35Binaries)
                                    let assembly = TryLoadAssembly(file)
                                    where assembly != null
                                    select new { file, assembly };

                var entryPoint = assemblyFiles.First(x => x.file.EndsWith(_entryPointPackage + ".dll", StringComparison.OrdinalIgnoreCase));


                var entryPointAssembly = entryPoint.assembly;
                var entrypointVersion = entryPointAssembly.GetName().Version;

                var entrypointFile = entryPoint.file;

                _notifier.BootstraperIs(entrypointFile, entrypointVersion);

                return ExecuteEntryPoint(args, entryPointAssembly);
            }
            catch (Exception e)
            {
                return _notifier.RunFailed(e);
            }
        }

        static void EnsureDirectoryExists(string wrapsPath)
        {
            if (!Directory.Exists(wrapsPath))
                Directory.CreateDirectory(wrapsPath);
        }

        static Assembly TryLoadAssembly(string asm)
        {
            return Assembly.LoadFrom(asm);
        }

        void AddOpenWrapSystemPathToEnvironment(string openWrapRootPath)
        {
            var env = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            if (env != null && env.Contains(openWrapRootPath))
                return;
            Environment.SetEnvironmentVariable("PATH", env + ";" + openWrapRootPath, EnvironmentVariableTarget.User);
            _notifier.Message("Added '{0}' to PATH.", openWrapRootPath);
        }

        BootstrapResult ExecuteEntryPoint(string[] args, Assembly entryPointAssembly)
        {
            var entryPointMethod = (
                                           from exportedType in entryPointAssembly.GetExportedTypes()
                                           where exportedType.Name.EndsWith("Runner")
                                           let mainMethod = exportedType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string[]) }, null)
                                           where mainMethod != null
                                           select mainMethod
                                   )
                    .First();

            return (BootstrapResult)entryPointMethod.Invoke(null, new object[] { args });
        }

        IEnumerable<string> GetAssemblyPathsForProjectRepository()
        {
            return
                    (
                            from directory in GetSelfAndParents(Environment.CurrentDirectory)
                            where directory.Exists
                            let wrapDirectory = GetCacheDirectoryFromOpenWrapSystemDirectory(directory)
                            where wrapDirectory != null && wrapDirectory.Exists
                            from uncompressedFolder in wrapDirectory.GetDirectories()
                            let match = MatchFolderName(uncompressedFolder)
                            where match.Success
                            let version = new Version(match.Groups["version"].Value)
                            let name = match.Groups["name"].Value
                            group new { name, uncompressedFolder, version } by name
                            into tuplesByName
                            select tuplesByName.OrderByDescending(x => x.version).First().uncompressedFolder.FullName
                    ).OrderByDescending(x => x).ToList();
        }

        IEnumerable<string> GetAssemblyPathsForSystemRepository()
        {
            return (
                           from uncompressedFolder in UncompressedUserDirectories()
                           let match = MatchFolderName(uncompressedFolder)
                           where match.Success
                           let version = new Version(match.Groups["version"].Value)
                           let name = match.Groups["name"].Value
                           group new { name, folder = uncompressedFolder.FullName, version } by name
                           into tuplesByName
                           select tuplesByName.OrderByDescending(x => x.version).First().folder
                   ).ToList();
        }

        DirectoryInfo GetCacheDirectoryFromOpenWrapSystemDirectory(DirectoryInfo directory)
        {
            try
            {
                if (directory == null) return null;
                var cacheDirectory = new DirectoryInfo(Path.Combine(Path.Combine(directory.FullName, "wraps"), "_cache"));
                if (cacheDirectory.Exists)
                    return cacheDirectory;
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        IEnumerable<DirectoryInfo> GetSelfAndParents(string directoryPath)
        {
            var directory = new DirectoryInfo(directoryPath);
            do
            {
                yield return directory;
                directory = directory.Parent;
            } while (directory != null);
        }

        void InstallFreshVersion()
        {
            switch (_notifier.InstallOptions())
            {
                case InstallAction.InstallToDefaultLocation:
                    InstallToDefaultLocation();
                    break;
                case InstallAction.UseCurrentExecutableLocation:
                    InstallLinkToCurrentVersion();
                    break;
            }
        }

        void InstallLinkToCurrentVersion()
        {
            var path = _currentExecutable;
            if (!path.Exists)
                throw new FileNotFoundException("The console executable is not on a local file system.");

            var linkContent = Encoding.UTF8.GetBytes(path.FullName);
            using (var file = File.Create(Path.Combine(_rootPath, _currentExecutable.Name + ".link")))
                file.Write(linkContent, 0, linkContent.Length);
            AddOpenWrapSystemPathToEnvironment(path.Directory.FullName);
        }

        void InstallToDefaultLocation()
        {
            var file = _currentExecutable;
            if (!file.Exists)
                throw new FileNotFoundException("Couldn't find the bootstrapper executable.");

            System.Console.WriteLine("Installing the shell to '{0}'.", _rootPath);
            if (!Directory.Exists(_rootPath))
                Directory.CreateDirectory(_rootPath);
            file.CopyTo(Path.Combine(_rootPath, file.Name));

            AddOpenWrapSystemPathToEnvironment(_rootPath);
        }

        Match MatchFolderName(DirectoryInfo uncompressedFolder)
        {
            return _openwrapRegex.Match(uncompressedFolder.Name);
        }

        IEnumerable<string> TryDownloadOpenWrap()
        {
            EnsureDirectoryExists(_wrapsPath);
            _notifier.Message("OpenWrap packages not found. Attempting download.");
            var client = new NotifyProgressWebClient(_notifier);

            var packagesToDownload = client.DownloadString(new Uri(_bootstrapAddress, UriKind.Absolute))
                    .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(x => !x.StartsWith("#"))
                    .Select(x => new Uri(x, UriKind.Absolute));

            foreach (var packageUri in packagesToDownload)
            {
                var packageClient = new NotifyProgressWebClient(_notifier);
                var fileName = packageUri.Segments.Last();
                string wrapFilePath = Path.Combine(_wrapsPath, fileName);

                if (File.Exists(wrapFilePath))
                    File.Delete(wrapFilePath);

                packageClient.DownloadFile(packageUri, wrapFilePath);

                _notifier.Message("Expanding package...");
                var extractFolder = Path.Combine(_cachePath, Path.GetFileNameWithoutExtension(fileName));

                if (Directory.Exists(extractFolder))
                    Directory.Delete(extractFolder, true);

                EnsureDirectoryExists(extractFolder);

                using (var wrapFileStream = File.OpenRead(wrapFilePath))
                    ZipArchive.Extract(wrapFileStream, extractFolder);
            }
            return GetAssemblyPathsForSystemRepository();
        }

        void TryUpgrade(string consolePath)
        {
            var existingVersion = new Version(FileVersionInfo.GetVersionInfo(consolePath).FileVersion);
            var currentVersion = new Version(FileVersionInfo.GetVersionInfo(_currentExecutable.FullName).FileVersion);

            if (currentVersion > existingVersion)
            {
                _notifier.Message("Upgrading '{0}' => '{1}'", existingVersion, currentVersion);
                File.Copy(_currentExecutable.FullName, consolePath, true);
            }
        }

        IEnumerable<DirectoryInfo> UncompressedUserDirectories()
        {
            var di = new DirectoryInfo(Path.Combine(_wrapsPath, "_cache"));
            if (di.Exists)
                return di.GetDirectories();
            return Enumerable.Empty<DirectoryInfo>();
        }

        void VerifyConsoleInstalled()
        {
            string oPath = Path.Combine(_rootPath, _currentExecutable.Name);
            string linkPath = Path.Combine(_rootPath, _currentExecutable.Name + ".link");
            if (!File.Exists(oPath))
            {
                if (!File.Exists(linkPath))
                    InstallFreshVersion();
                else
                    TryUpgrade(File.ReadAllText(linkPath, Encoding.UTF8));
            }
            else
            {
                TryUpgrade(oPath);
            }
        }

        public class NotifyProgressWebClient
        {
            readonly ManualResetEvent _completed = new ManualResetEvent(false);
            readonly INotifier _notifier;
            readonly WebClient _webClient = new WebClient();
            Exception _error;
            int _progress;
            string _stringReadResult;

            public NotifyProgressWebClient(INotifier notifier)
            {
                _notifier = notifier;
                _webClient.DownloadFileCompleted += DownloadFileCompleted;
                _webClient.DownloadStringCompleted += DownloadStringCompleted;
                _webClient.DownloadProgressChanged += DownloadProgressChanged;
            }

            public void DownloadFile(Uri uri, string destinationFile)
            {
                _notifier.DownloadStart(uri);

                _webClient.DownloadFileAsync(uri, destinationFile);
                Wait();
            }

            public string DownloadString(Uri uri)
            {
                _notifier.DownloadStart(uri);

                _webClient.DownloadStringAsync(uri);
                Wait();
                return _stringReadResult;
            }

            void Completed(AsyncCompletedEventArgs e)
            {
                _notifier.DownloadEnd();
                if (e.Error != null)
                    _error = e.Error;
                _completed.Set();
            }

            void Wait()
            {
                _completed.WaitOne();
                if (_error != null)
                    throw new WebException("An error occured.", _error);
            }

            void DownloadFileCompleted(object src, AsyncCompletedEventArgs e)
            {
                Completed(e);
            }

            void DownloadProgressChanged(object src, DownloadProgressChangedEventArgs e)
            {
                _notifier.DownloadProgress(e.ProgressPercentage);
            }

            void DownloadStringCompleted(object src, DownloadStringCompletedEventArgs e)
            {
                _stringReadResult = e.Result;
                Completed(e);
            }
        }
    }
}
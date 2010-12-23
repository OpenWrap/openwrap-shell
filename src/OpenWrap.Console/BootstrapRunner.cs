using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenWrap.Preloading;

namespace OpenWrap
{
    public class BootstrapRunner
    {
        readonly string _entryPointPackage;
        readonly string _executableName;
        readonly INotifier _notifier;
        readonly IEnumerable<string> _packageNamesToLoad;
        string _bootstrapAddress;
        FileInfo _currentExecutable;
        string _systemRootPath;

        public BootstrapRunner(string executableName, string systemRootPath, IEnumerable<string> packageNamesToLoad, string bootstrapAddress, INotifier notifier)
        {
            _packageNamesToLoad = packageNamesToLoad;
            _systemRootPath = systemRootPath;
            _notifier = notifier;
            _entryPointPackage = packageNamesToLoad.First();
            _bootstrapAddress = bootstrapAddress;
            _executableName = executableName;
        }

        public BootstrapResult Run(string[] args)
        {
            if (args.Contains("-debug", StringComparer.OrdinalIgnoreCase))
            {
                Debugger.Launch();
                args = args.Where(x => x.IndexOf("-debug", StringComparison.OrdinalIgnoreCase) == -1).ToArray();
            }
            args = ProcessArgumentWithValue(args, "-bootstrap-href", x => _bootstrapAddress = x);
            args = ProcessArgumentWithValue(args, "-bootstrap-sysroot", x => _systemRootPath = x);


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
                var bootstrapPackages = Preloader.GetPackageFolders(Preloader.RemoteInstall.FromServer(_bootstrapAddress, _notifier),
                                                                    Path.Combine(_systemRootPath, "wraps"),
                                                                    _packageNamesToLoad.ToArray());

                if (bootstrapPackages.Count() == 0)
                    throw new EntryPointNotFoundException("Could not find OpenWrap assemblies in either current project or system repository.");

                var assemblyFiles = Preloader.LoadAssemblies(bootstrapPackages);

                var entryPoint = assemblyFiles.First(x => x.Value.EndsWith(_entryPointPackage + ".dll", StringComparison.OrdinalIgnoreCase));


                var entryPointAssembly = entryPoint.Key;
                var entrypointVersion = entryPointAssembly.GetName().Version;

                var entrypointFile = entryPoint.Value;

                _notifier.BootstraperIs(entrypointFile, entrypointVersion);

                return ExecuteEntryPoint(args, entryPointAssembly);
            }
            catch (Exception e)
            {
                return _notifier.RunFailed(e);
            }
        }

        void AddOpenWrapSystemPathToEnvironment(string openWrapRootPath)
        {
            var env = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            if (env != null && env.Contains(openWrapRootPath))
                return;
            Environment.SetEnvironmentVariable("PATH", env + ";" + openWrapRootPath, EnvironmentVariableTarget.User);
            _notifier.Message("Added '{0}' to PATH.", openWrapRootPath);
        }

        static BootstrapResult ExecuteEntryPoint(string[] args, Assembly entryPointAssembly)
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
            using (var file = File.Create(Path.Combine(_systemRootPath, _currentExecutable.Name + ".link")))
                file.Write(linkContent, 0, linkContent.Length);
            AddOpenWrapSystemPathToEnvironment(path.Directory.FullName);
        }

        void InstallToDefaultLocation()
        {
            var file = _currentExecutable;
            if (!file.Exists)
                throw new FileNotFoundException("Couldn't find the bootstrapper executable.");

            Console.WriteLine("Installing the shell to '{0}'.", _systemRootPath);
            if (!Directory.Exists(_systemRootPath))
                Directory.CreateDirectory(_systemRootPath);


            var targetExecutableName = Path.Combine(_systemRootPath, _executableName);
            file.CopyTo(targetExecutableName);
            CopyManifest(targetExecutableName);

            AddOpenWrapSystemPathToEnvironment(_systemRootPath);
        }

        static void CopyManifest(string targetExecutableName)
        {
            using (var stream = File.Open(targetExecutableName + ".config", FileMode.Create, FileAccess.Write))
            {
                var content = Encoding.UTF8.GetBytes(MANIFEST_NET_VERSION);
                stream.Write(content, 0, content.Length);
            }
        }

        const string MANIFEST_NET_VERSION =
@"<?xml version =""1.0""?>
<configuration>
    <startup useLegacyV2RuntimeActivationPolicy=""true"">
    <supportedRuntime version=""v4.0""/>
    </startup>
</configuration>";
        string[] ProcessArgumentWithValue(string[] args, string argumentName, Action<string> argValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    argValue(args[i + 1]);
                    args = args.Take(i).Concat(args.Skip(i + 1)).ToArray();
                    break;
                }
            }
            return args;
        }

        void TryUpgrade(string consolePath)
        {
            var existingVersion = new Version(FileVersionInfo.GetVersionInfo(consolePath).FileVersion);
            var currentVersion = new Version(FileVersionInfo.GetVersionInfo(_currentExecutable.FullName).FileVersion);

            if (currentVersion > existingVersion)
            {
                _notifier.Message("Upgrading '{0}' => '{1}'", existingVersion, currentVersion);
                File.Copy(_currentExecutable.FullName, consolePath, true);
                CopyManifest(consolePath);
            }
        }

        void VerifyConsoleInstalled()
        {
            string existingBootstrapperPath = Path.Combine(_systemRootPath, _executableName);
            string linkPath = Path.Combine(_systemRootPath, _executableName + ".link");
            if (!File.Exists(existingBootstrapperPath))
            {
                if (!File.Exists(linkPath))
                    InstallFreshVersion();
                else
                    TryUpgrade(File.ReadAllText(linkPath, Encoding.UTF8));
            }
            else
            {
                TryUpgrade(existingBootstrapperPath);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
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
        string _proxyUsername;
        string _proxyPassword;
        string _proxy;
        string _shellInstall;
        bool _shellPanic;
        bool _useSystem;
        string _shellVersion;
        const byte CACHE_VERSION = 1;

        public BootstrapRunner(string executableName, string systemRootPath, IEnumerable<string> packageNamesToLoad, string bootstrapAddress, INotifier notifier)
        {
            _packageNamesToLoad = packageNamesToLoad;
            _systemRootPath = systemRootPath;
            _notifier = notifier;
            _entryPointPackage = packageNamesToLoad.First();
            _bootstrapAddress = bootstrapAddress;
            _executableName = executableName;
            _shellVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion;
        }

        public BootstrapResult Run(string[] args)
        {
            var consumedArgs = new List<string>();
            if (args.Contains("-ShellDebug", StringComparer.OrdinalIgnoreCase))
            {
                // mono doesn't support attaching a debugger
                if (Type.GetType("Mono.Runtime") == null && !Debugger.IsAttached)
                    Debugger.Launch();
                consumedArgs.Add("ShellDebug");
                _debug = true;
                args = args.Where(x => x.IndexOf("-ShellDebug", StringComparison.OrdinalIgnoreCase) == -1).ToArray();
            }
            args = ProcessArgumentWithValue(args, "-InstallHref", x =>
            {
                _bootstrapAddress = x;
                consumedArgs.Add("InstallHref");
            });
            args = ProcessArgumentWithValue(args, "-SystemRepositoryPath", x =>
            {
                _systemRootPath = x;
                consumedArgs.Add("SystemRepositoryPath");
            });
            ProcessArgumentWithValue(args, "-ProxyUsername", x =>
            {
                _proxyUsername = x;
                consumedArgs.Add("ProxyUsername");
            });
            ProcessArgumentWithValue(args, "-ProxyPassword", x =>
            {
                _proxyPassword = x;
                consumedArgs.Add("ProxyPassword");
            });
            ProcessArgumentWithValue(args, "-ProxyHref", x =>
            {
                _proxy = x;
                consumedArgs.Add("ProxyHref");
            });
            args = ProcessArgumentWithValue(args, "-ShellInstall", x =>
            {
                _shellInstall = x;
                consumedArgs.Add("ShellInstall");
            });
            args = ProcessArgumentWithoutValue(args, "-ShellPanic", () =>
            {
                _shellPanic = true;
                consumedArgs.Add("ShellPanic");
            });
            args = ProcessArgumentWithoutValue(args, "-UseSystem", () =>
            {
                _useSystem = true;
                consumedArgs.Add("UseSystem");
            });



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
                var systemWrapFiles = Path.Combine(_systemRootPath, "wraps");
                if (_shellPanic)
                    TryRemoveWrapFiles(_packageNamesToLoad, systemWrapFiles);
                var bootstrapPackages = Preloader.GetPackageFolders(Preloader.RemoteInstall.FromServer(_bootstrapAddress, _notifier, _proxy, _proxyUsername, _proxyPassword),
                                                                    _useSystem ? null : Environment.CurrentDirectory,
                                                                    _systemRootPath,
                                                                    _packageNamesToLoad.ToArray());

                if (bootstrapPackages.Count() == 0)
                    throw new EntryPointNotFoundException("Could not find OpenWrap assemblies in either current project or system repository.");
                LogFoundPackages(bootstrapPackages);
                var assemblyFiles = Preloader.LoadAssemblies(bootstrapPackages);

                LogLoadedAssemblies(assemblyFiles);

                var entry = FindEntrypoint(assemblyFiles.Select(_ => _.Key));
                if (entry != null)
                {
                    NotifyVersion(entry.Value.Key.Assembly);
                    return ExecuteEntrypoint(entry.Value, assemblyFiles.Select(_ => _.Key), consumedArgs);
                }

                var entryPoint = FindLegacyEntrypoint(assemblyFiles.Select(_ => _.Key));
                if (entryPoint.Key != null)
                {
                    NotifyVersion(entryPoint.Key.Assembly);
                    return ExecuteEntrypoint(args, entryPoint);
                }
                return BootstrapResult.EntrypointNotFound;
            }
            catch (Exception e)
            {
                return _notifier.RunFailed(e);
            }
        }

        void LogLoadedAssemblies(IEnumerable<KeyValuePair<Assembly, string>> assemblyFiles)
        {
            if (!_debug) return;

            foreach (var loadedAssembly in assemblyFiles)
                Console.WriteLine("Pre-loaded assembly " + loadedAssembly.Value);
        }

        void LogFoundPackages(IEnumerable<string> bootstrapPackages)
        {
            if (!_debug) return;
            foreach (var pack in bootstrapPackages) Console.WriteLine("Detected package " + pack);
        }

        void NotifyVersion(Assembly assembly)
        {
            SemanticVersion fileVersion = null;
            try
            {
                var version = FileVersionInfo.GetVersionInfo(assembly.Location);
                fileVersion = SemanticVersion.TryParseExact(version.FileVersion);
            }
            catch
            {
            }
            if (fileVersion == null)
            {
                var attrib = Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute));
                if (attrib != null)
                    fileVersion = SemanticVersion.TryParseExact(((AssemblyInformationalVersionAttribute)attrib).InformationalVersion);
            }
            if (fileVersion == null)
                fileVersion = SemanticVersion.TryParseExact(assembly.GetName().Version.ToString());
            _notifier.BootstraperIs(assembly.Location, fileVersion);
        }
        BootstrapResult ExecuteEntrypoint(string[] args, KeyValuePair<Type, Func<string[], int>> entryPoint)
        {
            return (BootstrapResult)entryPoint.Value(args);
        }

        BootstrapResult ExecuteEntrypoint(KeyValuePair<Type, Func<IDictionary<string, object>, int>> entryPoint, IEnumerable<Assembly> assemblies, IEnumerable<string> consumedArgs)
        {
            var info = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                    { "openwrap.syspath", _systemRootPath },
                    { "openwrap.cd", Environment.CurrentDirectory },
                    { "openwrap.shell.commandline", GetCommandLine() },
                    { "openwrap.shell.assemblies", assemblies.Select(x=>x.Location).ToList() },
                    { "openwrap.shell.version", _shellVersion},
                    { "openwrap.shell.args", consumedArgs.ToList() }
            };
            return (BootstrapResult)entryPoint.Value(info);
        }

        string GetCommandLine()
        {
            var line = Environment.CommandLine.TrimStart();
            var exePosition = line.IndexOf(_executableName, StringComparison.OrdinalIgnoreCase);
            if (exePosition == -1)
                return line.StartsWith("\"") ? line.Substring(line.IndexOf("\"",1) + 1) : line.Substring(line.IndexOf(" ") + 1);

            var processed = line.Substring(exePosition + _executableName.Length).TrimStart();
            return processed.StartsWith("\"") ? processed.Substring(1).TrimStart() : processed.TrimStart();
        }

        void TryRemoveWrapFiles(IEnumerable<string> packageNamesToLoad, string systemWrapFiles)
        {
            if (Directory.Exists(systemWrapFiles) == false)
                return;
            foreach (var package in packageNamesToLoad)
                foreach (var file in Directory.GetFiles(systemWrapFiles, package + "-*.wrap"))
                    try
                    {
                        File.Delete(file);
                        _notifier.Message("PANIC: deleted " + file);
                    }
                    catch
                    {
                        _notifier.Message("PANIC: Could not delete " + file);
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

        BinaryFormatter _delegateSerializer = new BinaryFormatter();
        bool _debug;

        KeyValuePair<Type,Func<IDictionary<string, object>, int>>? LoadEntrypointCache(string assemblyLocation, Assembly assembly)
        {
            var cachedDelegatePath = Path.Combine(Path.GetDirectoryName(assemblyLocation), "_" + Path.GetFileName(assemblyLocation) + ".entrypoint");
            bool discard = false;
            if (File.Exists(cachedDelegatePath))
            {
                using (var stream = File.OpenRead(cachedDelegatePath))
                {
                    byte[] header = new byte[2];
                    if (stream.Read(header, 0, 2) != 2 || header[0] != CACHE_VERSION) 
                        discard = true;
                    else
                    {
                        if (header[1] == 0) return default(KeyValuePair<Type, Func<IDictionary<string, object>, int>>);
                        try
                        {
                            var stringReader = new StreamReader(stream, Encoding.Unicode);
                            var methodDetails = stringReader.ReadToEnd();
                            if (!string.IsNullOrEmpty(methodDetails) && methodDetails.IndexOf("::") != -1)
                            {
                                var typeName = methodDetails.Substring(0, methodDetails.IndexOf("::"));
                                var methodName = methodDetails.Substring(methodDetails.IndexOf("::") + 2);
                                var type = assembly.GetType(typeName);
                                var method = type.GetMethod(methodName, new[] { typeof(IDictionary<string, object>) });
                                return new KeyValuePair<Type, Func<IDictionary<string, object>, int>>(type, env=>(int)method.Invoke(null,new object[]{env}));
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            File.Delete(cachedDelegatePath);
            return null;
        }
        KeyValuePair<Type,Func<IDictionary<string,object>, int>>? CreateEntrypointCache(string assemblyLocation, Assembly assembly)
        {
            try
            {
                var methodInfo = (from type in assembly.GetExportedTypes()
                                  where type.Name.EndsWith("Runner")
                                  let mi = type.GetMethod("Main", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IDictionary<string, object>) }, null)
                                  where mi != null
                                  select new{mi, type}).FirstOrDefault();

                
                using(var file = File.Create(Path.Combine(Path.GetDirectoryName(assemblyLocation), "_" + Path.GetFileName(assemblyLocation) + ".entrypoint")))
                {
                    file.Write(new byte[] { CACHE_VERSION, methodInfo == null ? (byte)0 : (byte)1 }, 0, 2);
                    if (methodInfo != null)
                    {
                        var contentToWrite = Encoding.Unicode.GetBytes(methodInfo.type.FullName + "::" + methodInfo.mi.Name);
                        file.Write(contentToWrite, 0, contentToWrite.Length);
                        return new KeyValuePair<Type, Func<IDictionary<string, object>, int>>(methodInfo.type, env => (int)methodInfo.mi.Invoke(null, new object[] { env }));
                    }
                }
            }
            catch
            {
            }
            return null;
        }
        KeyValuePair<Type, Func<IDictionary<string, object>, int>>? FindEntrypoint(IEnumerable<Assembly> assemblies)
        {
            return assemblies.Select(assembly=>LoadEntrypointCache(assembly.Location, assembly) ?? CreateEntrypointCache(assembly.Location, assembly))
                .Where(_=>_ != null && _.Value.Value != null)
                .FirstOrDefault();
        }
        IEnumerable<IEnumerable<Type>> AssemblyTypes(IEnumerable<Assembly> assemblies)
        {
            foreach (var assembly in assemblies)
            {
                Type[] exportedTypes;
                try
                {
                    exportedTypes = assembly.GetExportedTypes();
                }
                catch
                {
                    continue;
                }
                yield return exportedTypes;
            }
        }
        KeyValuePair<Type, Func<string[], int>> FindLegacyEntrypoint(IEnumerable<Assembly> assemblies)
        {

            var mainMethod = (from visibleTypes in AssemblyTypes(assemblies)
                              from type in visibleTypes
                              where type.Name.EndsWith("Runner")
                              let mi = type.GetMethod("Main", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string[]) }, null)
                              where mi != null
                              select mi).FirstOrDefault();

            if (mainMethod != null)
            {
                var setSysPathMethod = mainMethod.DeclaringType.GetMethod("SetSystemRepositoryPath", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);

                Func<string[], int> value = args => (int)mainMethod.Invoke(null, new object[] { args });
                if (setSysPathMethod != null)
                    value = args =>
                    {
                        setSysPathMethod.Invoke(null, new object[] { _systemRootPath });
                        return value(args);
                    };
                return new KeyValuePair<Type, Func<string[], int>>(
                        mainMethod.DeclaringType,
                        value);
            }
            return default(KeyValuePair<Type, Func<string[], int>>);
        }


        void InstallFreshVersion()
        {
            InstallAction? result = null;
            if (_shellInstall != null)
            {
                var firstValue = Enum.GetNames(typeof(InstallAction)).FirstOrDefault(x => x.StartsWith(_shellInstall, StringComparison.OrdinalIgnoreCase));
                result = firstValue != null ? (InstallAction)Enum.Parse(typeof(InstallAction), firstValue) : (InstallAction?)null;
            }
            result = result ?? _notifier.InstallOptions();
            switch (result)
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
            if (Directory.Exists(_systemRootPath) == false)
                Directory.CreateDirectory(_systemRootPath);
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
    <supportedRuntime version=""v2.0.50727""/>
    <supportedRuntime version=""v4.0""/>
    </startup>
</configuration>";

        static string[] ProcessArgumentWithoutValue(string[] args, string argumentName, Action argValue)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals(argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    argValue();
                    args = args.Take(i).Concat(args.Skip(i + 1)).ToArray();
                    break;
                }
            }
            return args;
        }

        static string[] ProcessArgumentWithValue(string[] args, string argumentName, Action<string> argValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    argValue(args[i + 1]);
                    args = args.Take(i).Concat(args.Skip(i + 2)).ToArray();
                    break;
                }
            }
            return args;
        }

        void TryUpgrade(string consolePath)
        {
            var existingVersion = SemanticVersion.TryParseExact(FileVersionInfo.GetVersionInfo(consolePath).FileVersion);
            var currentVersion = SemanticVersion.TryParseExact(FileVersionInfo.GetVersionInfo(_currentExecutable.FullName).FileVersion);

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

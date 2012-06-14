using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;
using NUnit.Framework;
using OpenWrap;
using OpenWrap.Preloading;
using Tests;

namespace Tests.contexts
{
    public class preloader : contexts.context
    {
        protected string SystemRepositoryPath;
        Dictionary<string, package> Packages = new Dictionary<string, package>(StringComparer.OrdinalIgnoreCase);

        XDocument indexFile;
        string RemotePath;
        protected IEnumerable<string> Directories;

        public preloader()
        {
            indexFile = XDocument.Parse("<package-list />");
            Preloader.Extractor = CopyFiles;
            RemotePath = CreateTempDirectory();
        }

        void CopyFiles(Stream source, string destinationPath)
        {
            var reader = new StreamReader(source);
            var sourcePackage = reader.ReadToEnd();
            var package = Packages[sourcePackage];
            var assemblyPath = Path.Combine(destinationPath, package.assemblyPath);
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            if (Directory.Exists(assemblyDirectory) == false)
                Directory.CreateDirectory(assemblyDirectory);
            using (var descriptor = new StreamWriter(Path.Combine(destinationPath, package.name + ".wrapdesc")))
                descriptor.Write("name: {0}\r\nsemantic-version: {1}\r\nversion: {1}\r\n{2}", package.name, package.version, package.content);
            using (var assembly = File.OpenWrite(assemblyPath))
                package.assemblyStream.CopyTo(assembly);
        }

        protected void given_remote_package(string name, string version, string[] dependencies = null, string assemblyPath = null, params Expression<Action<FluentTypeBuilder>>[] types)
        {
            dependencies = dependencies ?? new string[0];
            Packages[name + "-" + version] = new package
            {
                    name = name,
                    version = SemanticVersion.TryParseExact(version),
                    content = string.Join("\r\n", dependencies.Select(x=>"depends: " + x).ToArray()),
                    assemblyPath = assemblyPath,
                    assemblyStream = AssemblyBuilder.CreateAssemblyStream(Path.GetFileName(assemblyPath), types)
            };
            var packageFileName = name + "-" + version + ".wrap";
            File.AppendAllText(Path.Combine(RemotePath, packageFileName), name + "-" + version);
 
            indexFile.Root.Add(new XElement("wrap", 
                new XAttribute("name", name),
                new XAttribute("version", version),
                new XAttribute("semantic-version", version),
                new XElement("link",
                    new XAttribute("rel", "package"),
                    new XAttribute("href", packageFileName)
                    ),
                    dependencies.Select(x=>new XElement("depends", x))
            ));
        }

        protected void given_system_package(string name, string version, IEnumerable<string> dependencies, string assemblyPath)
        {
            dependencies = dependencies ?? Enumerable.Empty<string>();
            var cacheDirForPackage = Path.Combine(SystemRepositoryPath, "_cache", name + "-" + version);

            File.Create(Path.Combine(SystemRepositoryPath, name + "-" + version + ".wrap")).Close();
            Directory.CreateDirectory(cacheDirForPackage);
            var fullAssemblyPath = Path.Combine(cacheDirForPackage, assemblyPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullAssemblyPath));
            using (var assembly = File.OpenWrite(fullAssemblyPath))
                AssemblyBuilder.CreateAssembly(assembly, Path.GetFileName(assemblyPath));

            using (var descriptor = new StreamWriter(Path.Combine(cacheDirForPackage, name + ".wrapdesc")))
                descriptor.Write("name: {0}\r\nsemantic-version: {1}\r\nversion: {1}\r\n{2}", name, version, string.Join("\r\n:", dependencies.Select(x => "depends: " + x).ToArray()));
        }

        protected void when_getting_package_folders(params string[] packages)
        {
            indexFile.Save(Path.Combine(RemotePath, "index.wraplist"));
            Directories = Preloader.GetPackageFolders(Preloader.RemoteInstall.FromServer("file://" + RemotePath + "/", NullNotifier.Instance, null, null, null),
                                                  Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
                                                  SystemRepositoryPath,
                                                  packages).ToList();
        }

        protected void given_temp_system_repository()
        {
            SystemRepositoryPath = CreateTempDirectory();
        }

        string CreateTempDirectory()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempPath);
            return tempPath;
        }

        protected class package
        {
            public string name;
            public SemanticVersion version;
            public string assemblyPath;
            public Stream assemblyStream;
            public string content;
        }

        [TestFixtureTearDown]
        protected void cleanup()
        {
            try
            {
                Directory.Delete(SystemRepositoryPath, true);
                Directory.Delete(RemotePath, true);
            }
            catch { }
        }

        protected DirectoryInfo package_dir(string packageName)
        {
            return new DirectoryInfo(Path.Combine(SystemRepositoryPath, "wraps", "_cache", packageName));
        }

        protected FileInfo package_file(string packageName, string path)
        {
            return new FileInfo(Path.Combine(SystemRepositoryPath, "wraps", "_cache", packageName, path));
        }

        protected void should_have_dir(params string[] path)
        {
            Directories.ShouldContain(Path.Combine(path) + Path.DirectorySeparatorChar);
        }
    }
    public class NullNotifier : INotifyDownload
    {
        public static NullNotifier Instance = new NullNotifier();
        private NullNotifier(){}
        public void DownloadStart(Uri downloadAddress)
        {
        }

        public void DownloadEnd()
        {
        }

        public void DownloadProgress(int progressPercentage)
        {
        }
    }
}
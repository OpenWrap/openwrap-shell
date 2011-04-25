using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Tests;

namespace Tests.download_packages
{
    public class package_dependencies_are_loaded : contexts.preloader
    {
        public package_dependencies_are_loaded()
        {
            given_temp_system_repository();
            given_remote_package("sauron", "1.0", null, "bin-net35/sauron.dll");
            given_remote_package("one-ring", "1.0", new[] { "sauron" }, "bin-net35/one-ring.dll");
            when_getting_package_folders("one-ring");
        }

        [Test]
        public void dependency_assembly_is_extracted()
        {
            package_file("sauron-1.0", "bin-net35\\sauron.dll").Exists.ShouldBeTrue();
        }

        [Test]
        public void dependency_directory_is_returned()
        {
            Directories.ShouldContain(Path.Combine(SystemRepositoryPath, "_cache", "sauron-1.0"));
        }

        [Test]
        public void dependency_directory_is_created()
        {
            package_dir("sauron-1.0").Exists.ShouldBeTrue();
        }
    }
}
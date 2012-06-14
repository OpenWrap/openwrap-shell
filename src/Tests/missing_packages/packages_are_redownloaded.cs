using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Tests.missing_packages
{
    class packages_are_redownloaded : contexts.preloader
    {
        public packages_are_redownloaded()
        {
            given_temp_system_repository();
            given_system_package("one-ring", "1.0", new[] { "sauron" }, "bin-net35/one-ring.dll");
            given_remote_package("sauron", "1.0", null, "bin-net35/sauron.dll");
            given_remote_package("one-ring", "1.0", new[] { "sauron" }, "bin-net35/one-ring.dll");
            when_getting_package_folders("one-ring");
        }

        [Test]
        public void missing_dependecy_is_downloaded()
        {
            package_dir("sauron-1.0").Exists.ShouldBeTrue();
        }

        [Test]
        public void dependnecy_directory_is_found()
        {
            should_have_dir(package_dir("sauron-1.0").FullName);
        }
    }
}

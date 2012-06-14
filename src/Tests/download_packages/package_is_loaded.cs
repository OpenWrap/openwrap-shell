using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Xml.Linq;
using NUnit.Framework;
using Tests.contexts;

namespace Tests.download_packages
{
    public class package_is_loaded : preloader
    {
        public package_is_loaded()
        {
            given_temp_system_repository();
            given_remote_package("one-ring", "1.0", null, "bin-net35/one-ring.dll");
            when_getting_package_folders("one-ring");
        }

        [Test]
        public void assembly_is_extracted()
        {
            package_file("one-ring-1.0", "bin-net35\\one-ring.dll").Exists.ShouldBeTrue();
        }

        [Test]
        public void directory_is_returned()
        {
            should_have_dir(SystemRepositoryPath, "wraps", "_cache", "one-ring-1.0");
        }

        [Test]
        public void package_folder_is_created()
        {
            package_dir("one-ring-1.0").Exists.ShouldBeTrue();
        }
    }

}
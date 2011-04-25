using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tests
{
    public static class IOExtensions
    {

        public static long CopyTo(this Stream stream, Stream destinationStream)
        {
            var buffer = new byte[4096];
            int readCount = 0;
            long totalWritten = 0;
            while ((readCount = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalWritten += readCount;
                destinationStream.Write(buffer, 0, readCount);
            }

            return totalWritten;
        }
    }
}

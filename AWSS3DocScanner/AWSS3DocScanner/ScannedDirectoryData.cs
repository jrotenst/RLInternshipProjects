using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AWSS3DocScanner
{
    class ScannedDirectoryData
    {
        public string RootDirectory { get; set; }
        public int Scanned { get; set; }
        public int Updated { get; set; }
        public int Inserted { get; set; }
        public int Batch { get; set; }

        public ScannedDirectoryData(string rootDirectory)
        {
            RootDirectory = rootDirectory;
        }
    }
}

using System.Collections.Generic;

namespace BackupWatcher
{
    public class Paths
    {
        public List<SourcePathPair> Include { get; set; }
        public List<string> Exclude { get; set; }
    }
}
using System.Linq;

namespace BackupWatcher.Utilities
{
    public static class StringUtilities
    {
        public static string Repeat(this string s, int n)
        {
            return string.Concat(Enumerable.Repeat(s, n));
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BackupWatcher.Utilities
{
    public static class FileUtilities
    {
        private static readonly string INDENT = "    ";
        public static Paths Paths;

        public static IEnumerable<DirectoryInfo> EnumerateDirectories(string path)
        {
            try
            {
                return new DirectoryInfo(path).EnumerateDirectories();
            }
            catch
            {
                return new List<DirectoryInfo>();
            }
        }

        public static IEnumerable<FileInfo> EnumerateFiles(string path)
        {
            try
            {
                return new DirectoryInfo(path).EnumerateFiles();
            }
            catch
            {
                return new List<FileInfo>();
            }

        }

        public static bool IsInExcludedDirectory(string path)
        {
            return Paths.Exclude.Any(excludedPath => NormalizePath(path).StartsWith(excludedPath));
        }

        public static bool IsDirectory(string path) => Directory.Exists(path);
        public static bool IsFile(string path) => File.Exists(path);

        public static string GetPathType(string path)
        {
            try { return IsDirectory(path) ? "Directory" : "File"; }
            catch { return ""; }
        }

        public static bool TargetNeedsUpdate(FileSystemInfo source, FileSystemInfo target)
        {
            return source.LastWriteTimeUtc > target.LastWriteTimeUtc || (source is FileInfo sourceFile && target is FileInfo targetFile && (sourceFile.Length != targetFile.Length));
        }

        public static void CopyFile(string sourcePath, string targetPath)
        {
            try
            {
                var targetDirectoryName = new FileInfo(targetPath).DirectoryName;

                if (!Directory.Exists(targetDirectoryName))
                {
                    Directory.CreateDirectory(targetDirectoryName);
                }

                File.Copy(sourcePath, targetPath, true);
            }
            catch (FileNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        public static void CopyDirectory(string sourcePath, string targetPath, int depth = 0)
        {
            var logIndent = INDENT.Repeat(depth);

            if (IsInExcludedDirectory(sourcePath))
            {
                return;
            }

            Console.WriteLine($"{logIndent}Copying \"{sourcePath}\" to \"{targetPath}\"...");

            Directory.CreateDirectory(targetPath);
            Directory.SetLastWriteTimeUtc(targetPath, DateTime.UtcNow);

            Parallel.ForEach(EnumerateFiles(sourcePath), (sourceFile) =>
            {
                var targetFilePath = Path.Join(targetPath, sourceFile.Name);
                var targetFileInfo = new FileInfo(targetFilePath);

                if (!File.Exists(targetFilePath) || TargetNeedsUpdate(sourceFile, targetFileInfo))
                {
                    CopyFile(sourceFile.FullName, targetFilePath);
                }
            });

            Parallel.ForEach(EnumerateDirectories(sourcePath), (subdirectory) =>
            {
                var targetSubdirectoryPath = Path.Join(targetPath, subdirectory.Name);

                if (!Directory.Exists(targetSubdirectoryPath) || TargetNeedsUpdate(subdirectory, new DirectoryInfo(targetSubdirectoryPath)))
                {
                    CopyDirectory(subdirectory.FullName, targetSubdirectoryPath, depth);
                }
            });

            Console.WriteLine($"{logIndent}Copied \"{sourcePath}\" to \"{targetPath}\"");
        }

        public static string NormalizePath(string path)
        {
            return path.ToLower().Replace("/", "\\");
        }
    }
}
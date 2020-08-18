using BackupWatcher.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text.Json;
using System.Threading.Tasks;

namespace BackupWatcher
{
    public class BackupWatcher
    {
        const string INDENT = "    ";

        private Paths paths;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();

        public static void Main()
        {
            try
            {
                new BackupWatcher().Run();
            }
            catch (FormatException e)
            {
                Console.WriteLine(e);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public void Run()
        {
            //string[] args = Environment.GetCommandLineArgs();
            Console.WriteLine(Directory.GetCurrentDirectory());
            var configContents = File.ReadAllText("../../../config.json");
            var configPaths = DeserializeConfig(configContents);

            paths = new Paths()
            {
                Include = configPaths.Include.Select(includePath => new SourcePathPair(FileUtilities.NormalizePath(includePath.Source), FileUtilities.NormalizePath(includePath.Target))).ToList(),
                Exclude = configPaths.Exclude.Select(excludePath => FileUtilities.NormalizePath(excludePath)).ToList()
            };
            FileUtilities.Paths = paths;

            Console.WriteLine("Include: " + string.Join(", ", paths.Include.AsEnumerable()));
            Console.WriteLine("Exclude: " + string.Join(", ", paths.Exclude.AsEnumerable()));

            StartWatchers();
            Initialize();

            while (true) ;
        }

        private void StartWatchers()
        {
            foreach (var path in paths.Include)
            {
                var watcher = new FileSystemWatcher
                {
                    Path = path.Source,
                    IncludeSubdirectories = true,
                    NotifyFilter =
                        NotifyFilters.LastWrite |
                        NotifyFilters.FileName |
                        NotifyFilters.DirectoryName |
                        NotifyFilters.CreationTime
                };

                watcher.Changed += OnChanged;
                watcher.Created += OnCreated;
                watcher.Deleted += OnDeleted;
                watcher.Renamed += OnRenamed;
                watcher.EnableRaisingEvents = true;

                watchers.Add(watcher);
            }
        }

        private void Initialize()
        {
            foreach (var path in paths.Include)
            {
                if (FileUtilities.IsInExcludedDirectory(path.Source))
                {
                    continue;
                }

                var startTime = DateTime.Now;
                Console.WriteLine($"Initializing \"{path.Source}\"...");

                if (FileUtilities.IsDirectory(path.Source))
                {
                    InitializeDirectory(path.Source, path.Target);
                }
                else if (FileUtilities.IsFile(path.Source))
                {
                    // TODO: finish this case
                }

                var endTime = DateTime.Now;
                Console.WriteLine($"Initialized \"{path.Source}\" after {(endTime - startTime).TotalMinutes} minutes");
            }
        }

        private void InitializeDirectory(string sourcePath, string targetPath, int depth = 0)
        {
            var logIndent = INDENT.Repeat(depth);

            //Console.WriteLine($"{logIndent}Initializing \"{sourcePath}\"...");

            if (!Directory.Exists(sourcePath))
            {
                return;
            }

            if (FileUtilities.IsInExcludedDirectory(sourcePath))
            {
                return;
            }

            if (!Directory.Exists(targetPath) || FileUtilities.TargetNeedsUpdate(new DirectoryInfo(sourcePath), new DirectoryInfo(targetPath)))
            {
                FileUtilities.CopyDirectory(sourcePath, targetPath, depth + 1);
            }
            else
            {
                Parallel.ForEach(FileUtilities.EnumerateDirectories(sourcePath), (subdirectory) =>
                {
                    InitializeDirectory(subdirectory.FullName, Path.Join(targetPath, subdirectory.Name), depth + 1);
                });
            }

            //Console.WriteLine($"{logIndent}Initialized \"{sourcePath}\"");
        }

        private void LogEvent(FileSystemEventArgs eventArgs, string action, bool isRename = false)
        {
            var pathType = FileUtilities.GetPathType(eventArgs.FullPath);

            if (pathType.Length > 0)
            {
                pathType += " ";
            }

            var details = $"\"{eventArgs.FullPath}\"";

            if (isRename)
            {
                details += $" => \"{(eventArgs as RenamedEventArgs).OldFullPath}\"";
            }

            Console.WriteLine($"{pathType}{action}: {details}");
        }

        private Paths DeserializeConfig(string json)
        {
            return JsonSerializer.Deserialize<Paths>(json);
        }

        #region Event Handlers
        private void OnEvent(FileSystemEventArgs eventArgs, string actionType, Action<string, string> callback)
        {
            var sourcePath = FileUtilities.NormalizePath(eventArgs.FullPath);

            if (FileUtilities.IsInExcludedDirectory(sourcePath))
            {
                return;
            }

            LogEvent(eventArgs, actionType);

            var includedSourcePathPair = paths.Include.FirstOrDefault(sourcePathPair => sourcePath.StartsWith(sourcePathPair.Source));

            if (includedSourcePathPair == null)
            {
                return;
            }

            var sourceSubpath = sourcePath.Substring(includedSourcePathPair.Source.Length);
            var targetPath = Path.Join(includedSourcePathPair.Target, sourceSubpath);

            callback(sourcePath, targetPath);
        }

        public void OnCreated(object source, FileSystemEventArgs eventArgs)
        {
            OnEvent(eventArgs, "Created", (string sourcePath, string targetPath) =>
            {
                if (FileUtilities.IsFile(sourcePath))
                {
                    FileUtilities.CopyFile(sourcePath, targetPath);
                }
                else if (FileUtilities.IsDirectory(sourcePath))
                {
                    FileUtilities.CopyDirectory(sourcePath, targetPath);
                }
            });
        }

        public void OnChanged(object source, FileSystemEventArgs eventArgs)
        {
            OnEvent(eventArgs, "Changed", (string sourcePath, string targetPath) =>
            {
                if (FileUtilities.IsFile(sourcePath))
                {
                    FileUtilities.CopyFile(sourcePath, targetPath);
                }
            });
        }

        public void OnDeleted(object source, FileSystemEventArgs eventArgs)
        {
            OnEvent(eventArgs, "Deleted", (string sourcePath, string targetPath) =>
            {
                try
                {
                    File.Delete(targetPath);
                }
                catch
                {
                    try
                    {
                        Directory.Delete(targetPath, true);
                    }
                    catch { }
                }
            });
        }

        public void OnRenamed(object source, RenamedEventArgs eventArgs)
        {
            var newFullPath = eventArgs.FullPath;
            var directory = FileUtilities.IsFile(newFullPath) ? new FileInfo(newFullPath).Directory.FullName : new DirectoryInfo(newFullPath).Parent.FullName;
            var name = FileUtilities.IsFile(newFullPath) ? new FileInfo(eventArgs.OldFullPath).Name : new DirectoryInfo(newFullPath).Name;
            var preRenameArgs = new FileSystemEventArgs(eventArgs.ChangeType, directory, name);

            OnDeleted(source, preRenameArgs);
            OnCreated(source, eventArgs);
        }
        #endregion
    }
}
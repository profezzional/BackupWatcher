using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Permissions;
using System.Text.Json;

public class Watcher
{
    private Paths paths;


    public static void Main()
    {
        try
        {
            new Watcher().Run();
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
            Include = configPaths.Include.Select(includePath => new SourcePathPair(NormalizePath(includePath.Source), NormalizePath(includePath.Target))).ToList(),
            Exclude = configPaths.Exclude.Select(excludePath => NormalizePath(excludePath)).ToList()
        };

        Console.WriteLine("Include: " + string.Join(", ", paths.Include.AsEnumerable()));
        Console.WriteLine("Exclude: " + string.Join(", ", paths.Exclude.AsEnumerable()));

        Initialize();

        // TODO: make this account for multiple paths (task-queueing system of some sort?)
        using FileSystemWatcher watcher = new FileSystemWatcher
        {
            Path = paths.Include[0].Source,
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.LastAccess |
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

        while (true) ;
    }

    private void Initialize()
    {
        foreach (var path in paths.Include)
        {
            if (IsInExcludedDirectory(path.Source)) { continue; }

            if (IsDirectory(path.Source))
            {
                InitializeDirectory(path);
            }
            else if (IsFile(path.Source))
            {
                // TODO: finish this case
            }
        }
    }

    private void InitializeDirectory(SourcePathPair directoryPath)
    {
        Console.WriteLine("initializing " + directoryPath.Source);

        if (!Directory.Exists(directoryPath.Source))
        {
            return;
        }

        if (!Directory.Exists(directoryPath.Target) || !TargetNeedsUpdate(new DirectoryInfo(directoryPath.Source), new DirectoryInfo(directoryPath.Target)))
        {
            CopyDirectory(directoryPath.Source, directoryPath.Target);
        }

        Console.WriteLine("initialized " + directoryPath.Source);
    }

    #region Directory Utils
    private bool IsInExcludedDirectory(string path)
    {
        return paths.Exclude.Any(excludedPath => path.StartsWith(excludedPath));
    }

    private bool IsDirectory(string path) => Directory.Exists(path);
    private bool IsFile(string path) => File.Exists(path);

    private string GetPathType(string path)
    {
        try { return IsDirectory(path) ? "Directory" : "File"; }
        catch { return ""; }
    }

    private bool TargetNeedsUpdate(FileSystemInfo source, FileSystemInfo target)
    {
        return source.LastWriteTimeUtc > target.LastWriteTimeUtc || (source is FileInfo sourceFile && target is FileInfo targetFile && (sourceFile.Length != targetFile.Length));
    }

    private void CopyFile(string sourcePath, string targetPath)
    {
        var targetDirectoryName = new FileInfo(targetPath).DirectoryName;

        if (!Directory.Exists(targetDirectoryName))
        {
            Directory.CreateDirectory(targetDirectoryName);
        }

        File.Copy(sourcePath, targetPath, true);
    }

    private void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        var sourceDirectoryInfo = new DirectoryInfo(sourcePath);

        foreach (var sourceFile in sourceDirectoryInfo.EnumerateFiles())
        {
            var targetFilePath = Path.Join(targetPath, sourceFile.Name);
            var targetFileInfo = new FileInfo(targetFilePath);

            if (!File.Exists(targetFilePath) || !TargetNeedsUpdate(sourceFile, targetFileInfo))
            {
                CopyFile(sourceFile.FullName, targetFilePath);
            }
        }

        foreach (var subdirectory in sourceDirectoryInfo.EnumerateDirectories())
        {
            var targetSubdirectoryPath = Path.Join(targetPath, subdirectory.Name);

            if (!Directory.Exists(targetSubdirectoryPath) || !TargetNeedsUpdate(subdirectory, new DirectoryInfo(targetSubdirectoryPath)))
            {
                CopyDirectory(subdirectory.FullName, targetSubdirectoryPath);
            }
        }
    }

    private string NormalizePath(string path)
    {
        return path.Replace("/", "\\");
    }
    #endregion

    private void LogEvent(FileSystemEventArgs eventArgs, string action, bool isRename = false)
    {
        var pathType = GetPathType(eventArgs.FullPath);

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
        var sourcePath = NormalizePath(eventArgs.FullPath);

        if (IsInExcludedDirectory(sourcePath))
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
            if (IsFile(sourcePath))
            {
                CopyFile(sourcePath, targetPath);
            }
            else if (IsDirectory(sourcePath))
            {
                CopyDirectory(sourcePath, targetPath);
            }
        });
    }

    public void OnChanged(object source, FileSystemEventArgs eventArgs)
    {
        OnEvent(eventArgs, "Changed", (string sourcePath, string targetPath) =>
        {
            if (IsFile(sourcePath))
            {
                CopyFile(sourcePath, targetPath);
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
        var directory = IsFile(newFullPath) ? new FileInfo(newFullPath).Directory.FullName : new DirectoryInfo(newFullPath).Parent.FullName;
        var name = IsFile(newFullPath) ? new FileInfo(eventArgs.OldFullPath).Name : new DirectoryInfo(newFullPath).Name;
        var preRenameArgs = new FileSystemEventArgs(eventArgs.ChangeType, directory, name);

        OnDeleted(source, preRenameArgs);
        OnCreated(source, eventArgs);
    }
    #endregion
}

public class Paths
{
    public List<SourcePathPair> Include { get; set; }
    public List<string> Exclude { get; set; }
}

public class SourcePathPair
{
    public string Source { get; set; }
    public string Target { get; set; }

    public SourcePathPair() { }
    public SourcePathPair(string source, string destination)
    {
        Source = source;
        Target = destination;
    }

    public override string ToString()
    {
        return $"\"{Source}\" => \"{Target}\"";
    }
}
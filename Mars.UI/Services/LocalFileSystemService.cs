using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Mars.UI.Services;

public class FileItemInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime ModifiedDate { get; set; }
    public bool IsHidden { get; set; }
}

public class LocalFileSystemService
{
    public List<FileItemInfo> ListDirectory(string path, bool showHidden = false)
    {
        var items = new List<FileItemInfo>();
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) return items;

        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                bool hidden = (entry.Attributes & FileAttributes.Hidden) != 0;
                if (hidden && !showHidden) continue;

                try
                {
                    items.Add(new FileItemInfo
                    {
                        Name = entry.Name,
                        Path = entry.FullName,
                        IsDirectory = entry is DirectoryInfo,
                        Size = entry is FileInfo fi ? fi.Length : 0,
                        ModifiedDate = entry.LastWriteTime,
                        IsHidden = hidden
                    });
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return items
            .OrderByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Copy(IEnumerable<string> sources, string destFolder)
    {
        foreach (var src in sources)
        {
            string name = System.IO.Path.GetFileName(src);
            string target = System.IO.Path.Combine(destFolder, name);

            // Same file — auto-rename
            if (IsSamePath(src, target))
            {
                target = GetUniqueName(target);
                name = System.IO.Path.GetFileName(target);
            }

            if (Directory.Exists(src))
                CopyDirectory(src, target);
            else if (File.Exists(src))
                File.Copy(src, target, overwrite: true);
        }
    }

    public void Move(IEnumerable<string> sources, string destFolder)
    {
        foreach (var src in sources)
        {
            string name = System.IO.Path.GetFileName(src);
            string target = System.IO.Path.Combine(destFolder, name);

            // Same path — skip (nothing to do)
            if (IsSamePath(src, target)) continue;

            if (Directory.Exists(src))
            {
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(src, target);
            }
            else if (File.Exists(src))
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(src, target);
            }
        }
    }

    private static bool IsSamePath(string a, string b)
        => string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string GetUniqueName(string path)
    {
        string dir = System.IO.Path.GetDirectoryName(path)!;
        string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(path);
        string ext = System.IO.Path.GetExtension(path);
        
        int counter = 1;
        string candidate;
        do
        {
            string suffix = counter == 1 ? " - Copy" : $" - Copy ({counter})";
            candidate = System.IO.Path.Combine(dir, $"{nameNoExt}{suffix}{ext}");
            counter++;
        } while (File.Exists(candidate) || Directory.Exists(candidate));
        
        return candidate;
    }

    public void Delete(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                Directory.Delete(p, true);
            else if (File.Exists(p))
                File.Delete(p);
        }
    }

    public void Rename(string path, string newName)
    {
        string parent = System.IO.Path.GetDirectoryName(path)!;
        string newPath = System.IO.Path.Combine(parent, newName);

        if (Directory.Exists(path))
            Directory.Move(path, newPath);
        else if (File.Exists(path))
            File.Move(path, newPath);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    public string GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var parent = Directory.GetParent(path);
        return parent?.FullName ?? path;
    }

    public string GetHomePath() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public string[] GetDrives()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToArray();
        return new[] { "/" };
    }

    private void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(dir)));
    }
}

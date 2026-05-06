using System;
using System.IO;

namespace Mars.UI.Services;

public interface IPathsService
{
    string AppDirectory { get; }
    string ConfigDirectory { get; }
    string AssetsDirectory { get; }
    string LanguagesDirectory { get; }
}

public class PathsService : IPathsService
{
    public string AppDirectory { get; } = AppContext.BaseDirectory;
    
    public string ConfigDirectory { get; }
    
    public string AssetsDirectory { get; }
    
    public string LanguagesDirectory { get; }

    public PathsService()
    {
        // For settings and writeable data
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "MarsApp");
            
        // For read-only assets bundled with the app
        AssetsDirectory = Path.Combine(AppDirectory, "Assets");
        LanguagesDirectory = Path.Combine(AssetsDirectory, "Languages");
        
        CreateDir(ConfigDirectory);
    }

    private void CreateDir(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}
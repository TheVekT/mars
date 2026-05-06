using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Mars.UI.ViewModels;

public partial class FileItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private bool _isDirectory;
    [ObservableProperty] private long _size;
    [ObservableProperty] private DateTime _modifiedDate;
    [ObservableProperty] private bool _isHidden;
    [ObservableProperty] private bool _isSelected;

    public string Icon => IsDirectory ? "📁" : "📄";

    public string FormattedSize
    {
        get
        {
            if (IsDirectory) return "";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            if (Size < 1024L * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
            return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
        }
    }

    public string FormattedDate => ModifiedDate == default ? "" : ModifiedDate.ToString("dd.MM.yyyy HH:mm");
}

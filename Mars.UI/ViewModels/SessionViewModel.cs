using CommunityToolkit.Mvvm.ComponentModel;
using Mars.UI.Models;
using System;

namespace Mars.UI.ViewModels;

/// <summary>
/// Wrapper for <see cref="SessionModel"/> that provides change notifications for the UI.
/// </summary>
public partial class SessionViewModel : ObservableObject
{
    private readonly Action? _onChanged;
    public SessionModel Model { get; }

    public SessionViewModel(SessionModel model, Action? onChanged = null)
    {
        Model = model;
        _onChanged = onChanged;
    }

    private void NotifyChanged() => _onChanged?.Invoke();

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name != value)
            {
                Model.Name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                NotifyChanged();
            }
        }
    }

    public string IpAddress
    {
        get => Model.IpAddress;
        set
        {
            if (Model.IpAddress != value)
            {
                Model.IpAddress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NetworkAddress));
                NotifyChanged();
            }
        }
    }

    public string Port
    {
        get => Model.Port;
        set
        {
            if (Model.Port != value)
            {
                Model.Port = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NetworkAddress));
                NotifyChanged();
            }
        }
    }

    public bool IsFavourite
    {
        get => Model.IsFavourite;
        set
        {
            if (Model.IsFavourite != value)
            {
                Model.IsFavourite = value;
                OnPropertyChanged();
                NotifyChanged();
            }
        }
    }

    public DateTime LastConnectedTime
    {
        get => Model.LastConnectedTime;
        set
        {
            if (Model.LastConnectedTime != value)
            {
                Model.LastConnectedTime = value;
                OnPropertyChanged();
                NotifyChanged();
            }
        }
    }

    public string LastUsedPassword
    {
        get => Model.LastUsedPassword;
        set
        {
            if (Model.LastUsedPassword != value)
            {
                Model.LastUsedPassword = value;
                OnPropertyChanged();
                NotifyChanged();
            }
        }
    }

    // Calculated properties for the UI
    public string NetworkAddress => Model.NetworkAddress;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? NetworkAddress : Name;
}

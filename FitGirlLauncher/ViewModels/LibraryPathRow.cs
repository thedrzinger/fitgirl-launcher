using System.Collections.ObjectModel;
using System.Windows;
using FitGirlLauncher.Models;

namespace FitGirlLauncher.ViewModels;

/// <summary>One row in the Settings page's "Game library" list: a folder path plus its controls.</summary>
public class LibraryPathRow : ObservableObject
{
    private readonly ObservableCollection<LibraryPathRow> _owner;

    public LibraryPathRow(ObservableCollection<LibraryPathRow> owner)
    {
        _owner = owner;
        RemoveCommand = new RelayCommand(_ => _owner.Remove(this));
    }

    private string _path = "";
    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    /// <summary>
    /// Maintained by the owner collection via
    /// <see cref="SettingsViewModel"/>. The first row can't be removed so the
    /// list always keeps at least one row.
    /// </summary>
    private bool _isFirstRow;
    public bool IsFirstRow
    {
        get => _isFirstRow;
        set
        {
            if (SetProperty(ref _isFirstRow, value))
                OnPropertyChanged(nameof(RemoveButtonVisibility));
        }
    }

    public Visibility RemoveButtonVisibility =>
        IsFirstRow ? Visibility.Collapsed : Visibility.Visible;

    public RelayCommand RemoveCommand { get; }
}

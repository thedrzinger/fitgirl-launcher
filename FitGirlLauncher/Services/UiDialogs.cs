using System.Windows;
using Microsoft.Win32;

namespace FitGirlLauncher.Services;

/// <summary>Thin wrapper over the Win32 dialogs so view models stay testable-ish.</summary>
public static class UiDialogs
{
    public static string? ChooseFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public static void Info(string message, string title = "FitGirl Launcher")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Warn(string message, string title = "FitGirl Launcher")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static void Error(string message, string title = "FitGirl Launcher")
        => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}

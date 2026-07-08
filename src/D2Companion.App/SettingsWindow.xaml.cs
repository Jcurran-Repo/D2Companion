using System;
using System.Windows;
using D2Companion.App.ViewModels;

namespace D2Companion.App;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(AppSettings settings, string settingsPath, string referencePath)
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
        _viewModel = new SettingsViewModel(settings, settingsPath, referencePath);
        DataContext = _viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.Save();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't save settings: {ex.Message}", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

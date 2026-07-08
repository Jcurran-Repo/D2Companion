using System.Windows;
using System.Windows.Input;
using D2Companion.App.ViewModels;

namespace D2Companion.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TitleBarTheme.Apply(this);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // Push-to-talk: capture the mouse on press so we reliably get the release even if the
    // cursor drifts off the button, and drive one voice turn on release.
    private void TalkButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.BeginTalk();
        ((UIElement)sender).CaptureMouse();
    }

    private async void TalkButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ((UIElement)sender).ReleaseMouseCapture();
        if (ViewModel is not null)
            await ViewModel.EndTalkAsync();
    }
}

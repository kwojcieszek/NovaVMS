using Avalonia.Controls;
using Avalonia.Interactivity;
using NovaVMS.App.Localization;
using NovaVMS.App.Services;

namespace NovaVMS.App.Views;

/// <summary>
/// Modal login. Returns true once a user is logged in, false when the user chooses to exit.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly UserSessionService? _session;

    public LoginWindow()
    {
        InitializeComponent();
    }

    public LoginWindow(UserSessionService session, string userName)
        : this()
    {
        _session = session;
        UserNameBox.Text = userName;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (string.IsNullOrEmpty(UserNameBox.Text))
        {
            UserNameBox.Focus();
        }
        else
        {
            PasswordBox.Focus();
        }
    }

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (!_session.TryLogin(UserNameBox.Text ?? string.Empty, PasswordBox.Text ?? string.Empty))
        {
            ErrorText.Text = Localizer.Text("Login_Failed");
            ErrorText.IsVisible = true;
            PasswordBox.Text = string.Empty;
            PasswordBox.Focus();
            return;
        }

        if (AutoLoginBox.IsChecked == true)
        {
            try
            {
                await _session.SetAutoLoginAsync(true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                UnhandledExceptionLogger.Log("Saving automatic login", exception);
            }
        }

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}

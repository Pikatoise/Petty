using System.Windows;
using Petty.App.Online;

namespace Petty.App.Views.Online;

public partial class AccountWindow : OnlineWindow
{
    private readonly OnlineWindows _windows;

    internal AccountWindow(OnlineCoordinator online, OnlineWindows windows)
    {
        _windows = windows;
        InitializeComponent();
        InitializeOnline(online, requiresAccount: false);
        online.AccountChanged += UpdateAccount;
        Closed += (_, _) =>
        {
            online.AccountChanged -= UpdateAccount;
            PasswordBox.Clear();
            ConfirmBox.Clear();
        };
        UpdateAccount();
#if DEBUG
        var incoming = new System.Windows.Controls.Button();
        incoming.SetResourceReference(System.Windows.Controls.ContentControl.ContentProperty, "OnlineDebugIncoming");
        incoming.Click += (_, _) => _windows.ShowIncoming();
        AccountActions.Children.Add(incoming);
#endif
    }

    private void UpdateAccount()
    {
        bool signedIn = Online.Client.SessionId is not null;
        CredentialsPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        AccountActions.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        if (signedIn && Online.PublishedSessionId == Online.Client.SessionId && Online.Account is { } account)
        {
            AccountText.Text = account.Login;
        }
        else
        {
            AccountText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, signedIn ? "OnlineWorking" : "OnlineSignedOut");
        }
    }

    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        string login = LoginBox.Text.Trim();
        string password = PasswordBox.Password;
        PasswordBox.Clear();
        ConfirmBox.Clear();
        await RunAsync(async () =>
        {
            await Online.Client.LoginAsync(login, password, Lifetime);
            UpdateAccount();
        });
    }

    private async void OnRegister(object sender, RoutedEventArgs e)
    {
        string login = LoginBox.Text.Trim();
        string password = PasswordBox.Password;
        string confirmation = ConfirmBox.Password;
        PasswordBox.Clear();
        ConfirmBox.Clear();
        if (login.Length is < 3 or > 32 || login.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_') ||
            password.Length is < 12 or > 128 || password != confirmation)
        {
            Status("OnlineRegistrationInvalid");
            return;
        }
        if (await RunAsync(() => Online.Client.RegisterAsync(login, password, confirmation, Lifetime)))
        {
            Status("OnlineRegistered");
        }
    }

    private async void OnLogout(object sender, RoutedEventArgs e)
    {
        bool confirmed = await RunAsync(() => Online.Client.LogoutAsync(Lifetime));
        UpdateAccount();
        if (!confirmed && !IsClosed)
        {
            Status("OnlineLogoutUnconfirmed");
        }
    }

    private void OnFriends(object sender, RoutedEventArgs e) => _windows.ShowFriends();
    private void OnCompose(object sender, RoutedEventArgs e) => _windows.ShowCompose();
    private void OnSent(object sender, RoutedEventArgs e) => _windows.ShowHistory(false);
    private void OnReceived(object sender, RoutedEventArgs e) => _windows.ShowHistory(true);
}

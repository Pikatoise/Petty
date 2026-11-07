using System.Windows;
using Petty.App.Online;

namespace Petty.App.Views.Online;

public partial class AccountWindow : OnlineWindow
{
    internal void ShowFeedback(string key) => Status(key);
    private Guid? _displayedSession;
    private double _dashboardOffset;

    private void OnAuthPageChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }
        bool registering = RegistrationTab.IsChecked == true;
        ConfirmationPanel.Visibility = registering ? Visibility.Visible : Visibility.Collapsed;
        RegisterButton.Visibility = ConfirmationPanel.Visibility;
        RegisterButton.IsDefault = registering;
        SignInButton.Visibility = registering ? Visibility.Collapsed : Visibility.Visible;
        SignInButton.IsDefault = !registering;
        PageTitle.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty,
            registering ? "OnlineRegistrationPage" : "OnlineSignInPage");
        StatusText.Text = string.Empty;
        PasswordBox.Clear();
        ConfirmBox.Clear();
        LoginBox.Focus();
    }

    internal AccountWindow(OnlineCoordinator online)
    {
        InitializeComponent();
        InitializeOnline(online, requiresAccount: false, preferredWidth: () => online.Client.SessionId is null ? 380 : 520);
        DashboardPage.Initialize(online, RunAsync, Lifetime);
        ConversationPage.Initialize(online, RunAsync, Lifetime);
        DashboardPage.OpenConversation += ShowConversation;
        DashboardPage.Logout += OnLogout;
        ConversationPage.Back += ShowDashboard;
        online.AccountChanged += UpdateAccount;
        online.FriendsChanged += CheckConversation;
        online.Client.SessionChanged += SessionChanged;
        Closed += (_, _) =>
        {
            online.AccountChanged -= UpdateAccount;
            online.FriendsChanged -= CheckConversation;
            online.Client.SessionChanged -= SessionChanged;
            DashboardPage.Dispose();
            ConversationPage.Reset();
            PasswordBox.Clear();
            ConfirmBox.Clear();
        };
        UpdateAccount();

    }

    private void UpdateAccount()
    {
        Guid? session = Online.Client.SessionId;
        if (_displayedSession != session)
        {
            _displayedSession = session;
            ConversationPage.Reset();
            DashboardPage.Reset();
            _dashboardOffset = 0;
            ShowDashboard();
            PasswordBox.Clear();
            ConfirmBox.Clear();
            LoginBox.Clear();
        }
        bool signedIn = session is not null;
        if (signedIn)
        {
            DashboardPage.RefreshSnapshot();
        }
        AuthPage.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        AuthNavigation.Visibility = AuthPage.Visibility;
        if (!signedIn)
        {
            DashboardPage.Visibility = Visibility.Collapsed;
            ConversationPage.Visibility = Visibility.Collapsed;
            OnAuthPageChanged(this, new RoutedEventArgs());
        }
        UpdateWindowSize();
    }

    private void UpdateWindowSize()
    {
        bool signedIn = Online.Client.SessionId is not null;
        Width = Math.Min(signedIn ? 520 : 380, MaxWidth);
        Height = Math.Min(signedIn ? 580 : 480, MaxHeight);
    }

    private void SessionChanged() => Dispatcher.BeginInvoke(() => { if (!IsClosed) { UpdateAccount(); } });

    private void CheckConversation()
    {
        if (ConversationPage.CorrespondentId is { } id && !Online.Friends.Any(friend => friend.UserId == id))
        {
            ConversationPage.Reset();
            ShowDashboard();
        }
    }

    private void ShowDashboard()
    {
        ConversationPage.Visibility = Visibility.Collapsed;
        DashboardPage.Visibility = Online.Client.SessionId is null ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            if (DashboardPage.Visibility == Visibility.Visible)
            {
                DashboardPage.UpdateLayout();
                DashboardPage.ScrollOffset = _dashboardOffset;
            }
        });
        StatusText.Text = string.Empty;
    }

    private async void ShowConversation(Petty.Online.Client.Friend friend)
    {
        _dashboardOffset = DashboardPage.ScrollOffset;
        DashboardPage.Visibility = Visibility.Collapsed;
        ConversationPage.Visibility = Visibility.Visible;
        await ConversationPage.OpenAsync(friend);
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
            SignInTab.IsChecked = true;
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

}

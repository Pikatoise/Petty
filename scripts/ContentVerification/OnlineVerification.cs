using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Petty.App.Online;
using Petty.App.Views.Online;
using Petty.Online.Client;

internal static partial class Program
{
    private sealed class OnlineMemoryStore : ITokenStore
    {
        public Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<SessionTokens?>(new SessionTokens
        {
            AccessToken = "verification-access",
            RefreshToken = "verification-refresh",
            SessionId = Guid.NewGuid(),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        public Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class OnlineVerificationHandler(Friend friend, DeliveryLetter letter) : HttpMessageHandler
    {
        public int UnreadCount { get; set; } = 2;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            object response = path switch
            {
                "/api/v1/friends" => new[] { friend },
                "/api/v1/friends/requests" => new FriendRequests([new FriendRequest(Guid.NewGuid(), friend, DateTimeOffset.UtcNow)], []),
                _ when path.StartsWith("/api/v1/letters/conversations/", StringComparison.Ordinal) => new LetterHistoryPage([
                    new LetterHistoryItem(letter.Id, friend.UserId, Guid.NewGuid(), letter.Body, letter.SentAtUtc, letter.SentAtUtc, friend)], null),
                _ when path.Contains("/quota/") => new MailQuota(UnreadCount, 10),
                "/api/v1/letters/read" => await ReadAsync(request, ct),
                _ => throw new InvalidOperationException("Unexpected verification request.")
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response, response.GetType()) };
        }

        private static async Task<ReadLettersResult> ReadAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using JsonDocument body = await JsonDocument.ParseAsync(await request.Content!.ReadAsStreamAsync(ct), cancellationToken: ct);
            return new ReadLettersResult(body.RootElement.GetProperty("letterIds").EnumerateArray()
                .Select(id => new ReadLetter(id.GetGuid(), DateTimeOffset.UtcNow)).ToArray(), 0, 10);
        }
    }

    private static void CheckOnlineLayout(string output, string language)
    {
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var friend = new Friend(Guid.NewGuid(), "friend_with_a_long_login", "Milo", "british-shorthair-blue");
        var letter = new DeliveryLetter(Guid.NewGuid(), "Hello! Привет! 🐈\nA letter stays plain text: <b>hello</b>.", DateTimeOffset.UtcNow);
        var handler = new OnlineVerificationHandler(friend, letter);
        var client = new OnlineClient(new Uri("https://example.test"), new OnlineMemoryStore(), handler);
        var coordinator = new OnlineCoordinator(client, action => action(), () => { });
        var account = new AccountWindow(coordinator);
        Render(account, Path.Combine(output, $"online-{language}-account.png"));
        CheckAuthenticationControls(account, "SignInButton");
        Require(account.ResizeMode == ResizeMode.NoResize && account.Width == 380 && account.Height == 480 &&
            ((FrameworkElement)account.FindName("AuthPage")).ActualWidth <= 380,
            "Authentication window is resizable or too wide.");
        Require(((FrameworkElement)account.FindName("ConfirmationPanel")).Visibility == Visibility.Collapsed,
            "Sign-in page shows password confirmation.");
        account.Close();
        account = new AccountWindow(coordinator);
        ((RadioButton)account.FindName("RegistrationTab")).IsChecked = true;
        Require(account.Width == 380 && account.Height == 480, "Registration did not retain compact window dimensions.");
        Require(((FrameworkElement)account.FindName("ConfirmationPanel")).Visibility == Visibility.Visible &&
            ((Button)account.FindName("RegisterButton")).IsDefault &&
            ((Button)account.FindName("SignInButton")).Visibility == Visibility.Collapsed,
            "Registration navigation did not switch the form and default action.");
        ((PasswordBox)account.FindName("PasswordBox")).Password = "test-password";
        ((PasswordBox)account.FindName("ConfirmBox")).Password = "test-password";
        Render(account, Path.Combine(output, $"online-{language}-registration.png"));
        CheckAuthenticationControls(account, "RegisterButton");
        foreach (string field in new[] { "PasswordBox", "ConfirmBox" })
        {
            var password = (PasswordBox)account.FindName(field);
            var host = (ScrollViewer)password.Template.FindName("PART_ContentHost", password);
            Require(host.ViewportHeight >= host.ExtentHeight && host.ViewportHeight > 0,
                $"{field} clips password glyphs: viewport {host.ViewportHeight}, content {host.ExtentHeight}.");
        }
        var navigation = (FrameworkElement)account.FindName("AuthNavigation");
        Require(navigation.ActualHeight > 0 && navigation.TranslatePoint(new Point(0, navigation.ActualHeight), (UIElement)account.Content).Y <=
            ((FrameworkElement)account.Content).ActualHeight + 1,
            "Registration navigation is clipped outside the window.");
        ((PasswordBox)account.FindName("PasswordBox")).Password = "test-password";
        ((PasswordBox)account.FindName("ConfirmBox")).Password = "test-password";
        ((RadioButton)account.FindName("SignInTab")).IsChecked = true;
        Require(account.Height == 480, "Switching authentication pages changed the window height.");
        Require(((PasswordBox)account.FindName("PasswordBox")).Password.Length == 0 &&
            ((PasswordBox)account.FindName("ConfirmBox")).Password.Length == 0 &&
            ((Button)account.FindName("SignInButton")).IsDefault,
            "Switching authentication pages retained passwords or the wrong default action.");
        account.Close();
        client.RestoreAsync().GetAwaiter().GetResult();
        coordinator.Publish(new AccountProfile(Guid.NewGuid(), "verification", "Milo", "british-shorthair-blue", null),
            [friend], new FriendRequests([new FriendRequest(Guid.NewGuid(), friend, DateTimeOffset.UtcNow)], []),
            null, client.SessionId, mailboxRefreshed: true);
        var visit = new IncomingVisit(new MailboxVisit(friend, [letter]));
        var compose = new ComposeLetterWindow(coordinator, friend.UserId);
        var incoming = new IncomingLettersWindow(coordinator, visit);
        Window[] social = [new AccountWindow(coordinator), compose, incoming];
        try
        {
            foreach (Window window in social)
            {
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                if (window is AccountWindow)
                {
                    coordinator.Publish(coordinator.Account, coordinator.Friends, coordinator.Requests, null,
                        client.SessionId, mailboxRefreshed: false, friendsRefreshed: true);
                }
                PumpUntil(() => ((UIElement)window.Content).IsEnabled);
                Require(string.IsNullOrEmpty(((TextBlock)window.FindName("StatusText")).Text), $"{window.GetType().Name} failed to load.");
                string preview = $"online-{language}-{Array.IndexOf(social, window)}-{window.GetType().Name}";
                Render(window, Path.Combine(output, preview + ".png"));
                window.Width = 340;
                window.Height = 260;
                Render(window, Path.Combine(output, preview + "-compact.png"));
                if (window is AccountWindow management)
                {
                    var dashboard = (SocialDashboard)management.FindName("DashboardPage");
                    foreach (string name in new[] { "FriendsSection", "IncomingSection", "OutgoingSection", "BlockedSection" })
                    {
                        ((Expander)dashboard.FindName(name)).IsExpanded = true;
                    }
                    ((FrameworkElement)window.Content).UpdateLayout();
                    var scroll = (ScrollViewer)dashboard.FindName("DashboardScroll");
                    Require(scroll.ScrollableHeight > 0, "Compact friends window cannot scroll its form and lists.");
                    scroll.ScrollToEnd();
                    ((FrameworkElement)window.Content).UpdateLayout();
                    Require(scroll.VerticalOffset > 0, "Compact friends lists are unreachable.");
                    ((Expander)dashboard.FindName("OutgoingSection")).IsExpanded = false;
                    ((FrameworkElement)management.Content).UpdateLayout();
                    double position = scroll.VerticalOffset;
                    management.GetType().GetMethod("ShowConversation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(management, [friend]);
                    PumpUntil(() => ((UIElement)management.Content).IsEnabled);
                    var conversation = (ConversationView)management.FindName("ConversationPage");
                    conversation.GetType().GetMethod("OnBack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(conversation, [null, new RoutedEventArgs()]);
                    PumpUntil(() => dashboard.Visibility == Visibility.Visible && Math.Abs(scroll.VerticalOffset - position) < 2);
                    Require(((Expander)dashboard.FindName("IncomingSection")).IsExpanded &&
                        !((Expander)dashboard.FindName("OutgoingSection")).IsExpanded &&
                        ((Expander)dashboard.FindName("BlockedSection")).IsExpanded,
                        "Conversation navigation changed independent dashboard expansion states.");
                }
            }
            ((TextBox)compose.FindName("BodyBox")).Text = "A valid letter 🐈";
            Require(((Button)compose.FindName("SendButton")).IsEnabled, "Compose did not enable a valid letter with available quota.");
            Require(((TextBlock)compose.FindName("QuotaText")).Text.Contains("2/10"), "Compose did not display server quota.");
            var refresh = (Button)compose.FindName("RefreshButton");
            handler.UnreadCount = 10;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => ((UIElement)compose.Content).IsEnabled);
            Require(!((Button)compose.FindName("SendButton")).IsEnabled, "Compose ignored exhausted quota.");
            handler.UnreadCount = 9;
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => ((UIElement)compose.Content).IsEnabled);
            Require(((Button)compose.FindName("SendButton")).IsEnabled, "Compose cannot recover after the recipient reads a letter.");
            incoming.Close();
            Require(!visit.Complete, "Closing incoming mail silently acknowledged it.");
            var reopened = new IncomingLettersWindow(coordinator, visit);
            bool completed = false;
            reopened.AllRead += () => completed = true;
            bool incomingClosed = false;
            reopened.Closed += (_, _) => incomingClosed = true;
            var cards = (StackPanel)reopened.FindName("LettersPanel");
            Require(((StackPanel)((Expander)((Border)cards.Children[0]).Child).Content).Children.OfType<Button>().Count() == 0,
                "Expanded mail still contains a redundant read button.");
            ((Expander)((Border)cards.Children[0]).Child).IsExpanded = true;
            PumpUntil(() => visit.Complete);
            Require(visit.Complete, "Expanding mail failed to acknowledge its snapshot.");
            Require(!incomingClosed && !completed, "Reading the last letter closed the window or completed the visit before the window closed.");
            reopened.Close();
            Require(incomingClosed && completed, "Closing fully read mail did not complete its visit.");
            bool sessionClosed = false;
            compose.Closed += (_, _) => sessionClosed = true;
            client.InvalidateAsync(client.SessionId!.Value).GetAwaiter().GetResult();
            PumpUntil(() => sessionClosed);
            Require(((FrameworkElement)social[0].FindName("AuthPage")).Visibility == Visibility.Visible &&
                ((FrameworkElement)social[0].FindName("DashboardPage")).Visibility == Visibility.Collapsed,
                "Session replacement retained the previous account's social page.");
        }
        finally
        {
            foreach (Window window in social.Where(window => window != incoming))
            {
                window.Close();
            }
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void CheckAuthenticationControls(AccountWindow window, string actionName)
    {
        var content = (FrameworkElement)window.Content;
        var navigation = (FrameworkElement)window.FindName("AuthNavigation");
        var action = (FrameworkElement)window.FindName(actionName);
        var status = (TextBlock)window.FindName("StatusText");
        foreach (string message in new[] { string.Empty, new string('x', 300) })
        {
            status.Text = message;
            // Reserve space for native window chrome instead of testing against the outer window size.
            var client = new Size(window.Width - 16, window.Height - 40);
            content.Measure(client);
            content.Arrange(new Rect(new Point(), client));
            content.UpdateLayout();
            double navigationTop = navigation.TranslatePoint(new Point(), content).Y;
            double navigationBottom = navigation.TranslatePoint(new Point(0, navigation.ActualHeight), content).Y;
            double actionBottom = action.TranslatePoint(new Point(0, action.ActualHeight), content).Y;
            Require(Math.Abs(navigationBottom - content.ActualHeight) < 1,
                "Authentication navigation is not pinned to the client area's bottom edge.");
            Require(action.ActualHeight > 0 && actionBottom <= navigationTop &&
                action.TranslatePoint(new Point(), content).Y >= 0,
                "Authentication submit action is clipped, including when feedback is visible.");
        }
        status.Text = string.Empty;
    }

    private static void PumpUntil(Func<bool> ready)
    {
        var frame = new DispatcherFrame();
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (ready() || DateTime.UtcNow >= deadline) { frame.Continue = false; } };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Require(ready(), "Online verification did not finish.");
    }
}

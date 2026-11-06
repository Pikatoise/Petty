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
                "/api/v1/letters/incoming" or "/api/v1/letters/outgoing" => new LetterHistoryPage([
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
        using var windows = new OnlineWindows(coordinator, _ => { });
        var account = new AccountWindow(coordinator, windows);
        Render(account, Path.Combine(output, $"online-{language}-account.png"));
        account.Close();
        client.RestoreAsync().GetAwaiter().GetResult();
        coordinator.Publish(new AccountProfile(Guid.NewGuid(), "verification", "Milo", "british-shorthair-blue", null),
            [friend], new FriendRequests([new FriendRequest(Guid.NewGuid(), friend, DateTimeOffset.UtcNow)], []),
            null, client.SessionId, mailboxRefreshed: true);
        var visit = new IncomingVisit(new MailboxVisit(friend, [letter]));
        var compose = new ComposeLetterWindow(coordinator, friend.UserId);
        var incoming = new IncomingLettersWindow(coordinator, visit);
        Window[] social = [new FriendsWindow(coordinator, windows), compose,
            new LettersHistoryWindow(coordinator, false), new LettersHistoryWindow(coordinator, true), incoming];
        try
        {
            foreach (Window window in social)
            {
                window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                if (window is FriendsWindow)
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
                if (window is FriendsWindow)
                {
                    var scroll = (ScrollViewer)window.FindName("FriendsScroll");
                    Require(scroll.ScrollableHeight > 0, "Compact friends window cannot scroll its form and lists.");
                    scroll.ScrollToEnd();
                    ((FrameworkElement)window.Content).UpdateLayout();
                    Require(scroll.VerticalOffset > 0, "Compact friends lists are unreachable.");
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
            social[0].Closed += (_, _) => sessionClosed = true;
            client.InvalidateAsync(client.SessionId!.Value).GetAwaiter().GetResult();
            PumpUntil(() => sessionClosed);
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

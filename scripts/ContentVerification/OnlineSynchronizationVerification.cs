using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Petty.App.Online;
using Petty.App.Views.Online;
using Petty.Online.Client;

internal static partial class Program
{
    private sealed class LoginSynchronizationHandler : HttpMessageHandler
    {
        private readonly Guid _owner = Guid.NewGuid();
        private readonly Friend _friend = new(Guid.NewGuid(), "prepared_friend", null, null);
        private readonly SessionTokens _tokens = new()
        {
            AccessToken = "new-session",
            RefreshToken = "new-refresh",
            SessionId = Guid.NewGuid(),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_tokens) });
            }
            if (request.Headers.Authorization?.Parameter != _tokens.AccessToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = JsonContent.Create(new { code = "SESSION_REVOKED" })
                });
            }
            object? body = path switch
            {
                "/api/v1/me" => new AccountProfile(_owner, "verification", null, null, null),
                "/api/v1/friends" => new[] { _friend },
                "/api/v1/friends/requests" => new FriendRequests([], []),
                "/api/v1/users/blocked" => Array.Empty<BlockedUser>(),
                "/api/v1/mailbox/next" => null,
                _ => throw new InvalidOperationException("Unexpected login synchronization request.")
            };
            return Task.FromResult(body is null ? new HttpResponseMessage(HttpStatusCode.NoContent) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) });
        }
    }

    private static void CheckLoginSynchronization()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var client = new OnlineClient(new Uri($"http://127.0.0.1:{port}"), new OnlineMemoryStore(), new LoginSynchronizationHandler());
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        bool staleRejected = false;
        var online = new OnlineCoordinator(client, action => dispatcher.BeginInvoke(action), () => staleRejected = true);
        var window = new AccountWindow(online);
        try
        {
            online.StartAsync().GetAwaiter().GetResult();
            PumpUntil(() => staleRejected && client.SessionId is null);
            ((TextBox)window.FindName("LoginBox")).Text = "verification";
            ((PasswordBox)window.FindName("PasswordBox")).Password = "TestPassword12345";
            ((Button)window.FindName("SignInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dashboard = (SocialDashboard)window.FindName("DashboardPage");
            PumpUntil(() => ((TextBlock)dashboard.FindName("UserLogin")).Text == "verification" &&
                ((ItemsControl)dashboard.FindName("FriendsList")).Items.Count == 1);
            Require(window.Width == 520 && window.Height == 580, "Successful login did not resize the social window.");
            client.InvalidateAsync(client.SessionId!.Value).GetAwaiter().GetResult();
            PumpUntil(() => window.Width == 380 && window.Height == 480);
        }
        finally
        {
            window.Close();
            Task stopped = online.DisposeAsync().AsTask();
            PumpUntil(() => stopped.IsCompleted);
            stopped.GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class DelayedFriendMutation : HttpMessageHandler
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Started
        {
            get; private set;
        }
        public int Calls
        {
            get; private set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/friends/requests",
                "Friends window made an unexpected redundant request.");
            Started = true;
            await Release.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { requestId = Guid.NewGuid() }) };
        }
    }

    private static void CheckOnlineSynchronization()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        using var handler = new DelayedFriendMutation();
        var client = new OnlineClient(new Uri("https://example.test"), new OnlineMemoryStore(), handler);
        client.RestoreAsync().GetAwaiter().GetResult();
        var online = new OnlineCoordinator(client, action => action(), () => { });
        var account = new AccountProfile(Guid.NewGuid(), "verification", "Milo", "british-shorthair-blue", null);
        var friend = new Friend(Guid.NewGuid(), "new_friend", "Bob", "british-shorthair-blue");
        online.Publish(account, [], new([], []), null, client.SessionId, mailboxRefreshed: true);
        var window = new AccountWindow(online);
        var dashboard = (SocialDashboard)window.FindName("DashboardPage");
        try
        {
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            dashboard.GetType().GetMethod("OnShowAdd", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(dashboard, [null, new RoutedEventArgs()]);
            Require(((FrameworkElement)dashboard.FindName("Modal")).Visibility == Visibility.Visible &&
                ((Button)dashboard.FindName("AddButton")).IsDefault,
                "Add friend overlay did not open with Enter submission enabled.");
            ((TextBox)dashboard.FindName("FriendLogin")).Text = "new_friend";
            ((Button)dashboard.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => handler.Started);
            Require(!((UIElement)window.Content).IsEnabled, "Delayed friend mutation did not hold the busy state.");
            ((Button)dashboard.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            online.Publish(account, [friend], new([], []), null, client.SessionId, mailboxRefreshed: false);
            var panel = (ItemsControl)dashboard.FindName("FriendsList");
            Require(panel.Items.Count == 1, "A friends update was lost while the window was busy.");
            object row = panel.Items[0];
            online.Publish(account, [friend], new([], []), null, client.SessionId, mailboxRefreshed: true);
            Require(ReferenceEquals(row, panel.Items[0]), "An unchanged mailbox publication recreated friend rows.");
            handler.Release.TrySetResult();
            PumpUntil(() => ((UIElement)window.Content).IsEnabled);
            Require(handler.Calls == 1, "Friends window fetched data already owned by the coordinator.");
            Require(((FrameworkElement)dashboard.FindName("Modal")).Visibility == Visibility.Collapsed,
                "Successful friend submission left its overlay open.");
        }
        finally
        {
            handler.Release.TrySetResult();
            window.Close();
            online.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

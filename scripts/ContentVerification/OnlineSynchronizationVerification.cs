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
        using var windows = new OnlineWindows(online, _ => { });
        var account = new AccountProfile(Guid.NewGuid(), "verification", "Milo", "british-shorthair-blue", null);
        var friend = new Friend(Guid.NewGuid(), "new_friend", "Bob", "british-shorthair-blue");
        online.Publish(account, [], new([], []), null, client.SessionId, mailboxRefreshed: true);
        var window = new FriendsWindow(online, windows);
        try
        {
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            ((TextBox)window.FindName("LoginBox")).Text = "new_friend";
            window.GetType().GetMethod("OnAdd", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(window, [null, new RoutedEventArgs()]);
            PumpUntil(() => handler.Started);
            Require(!((UIElement)window.Content).IsEnabled, "Delayed friend mutation did not hold the busy state.");
            online.Publish(account, [friend], new([], []), null, client.SessionId, mailboxRefreshed: false);
            var panel = (StackPanel)window.FindName("FriendsPanel");
            Require(panel.Children.OfType<Border>().Count() == 1, "A friends update was lost while the window was busy.");
            UIElement row = panel.Children[0];
            online.Publish(account, [friend], new([], []), null, client.SessionId, mailboxRefreshed: true);
            Require(ReferenceEquals(row, panel.Children[0]), "An unchanged mailbox publication recreated friend rows.");
            handler.Release.TrySetResult();
            PumpUntil(() => ((UIElement)window.Content).IsEnabled);
            Require(handler.Calls == 1, "Friends window fetched data already owned by the coordinator.");
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

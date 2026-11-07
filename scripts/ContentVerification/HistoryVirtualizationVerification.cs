using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Petty.App.Online;
using Petty.App.Views.Online;
using Petty.Online.Client;

internal static partial class Program
{
    private sealed class HistoryPageHandler : HttpMessageHandler
    {
        private readonly Friend _friend = new(Guid.NewGuid(), "history_friend", "Milo", "british-shorthair-blue");
        private readonly Guid _recipient = Guid.NewGuid();
        private readonly Guid[] _ids = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToArray();
        public Friend Correspondent => _friend;
        public Guid OwnerId => _recipient;
        public int Page
        {
            get; private set;
        }
        public bool Empty
        {
            get; set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.RequestUri!.AbsolutePath == $"/api/v1/letters/conversations/{_friend.UserId:D}", "Unexpected history request.");
            Require(request.RequestUri.Query.Contains($"cursor={(Empty || Page == 0 ? string.Empty : Page.ToString())}", StringComparison.Ordinal),
                "History pagination lost its server cursor.");
            int start = Page * 50;
            LetterHistoryItem[] items = Empty ? [] : Enumerable.Range(start, 50).Select(index => new LetterHistoryItem(
                _ids[index], index % 2 == 0 ? _friend.UserId : _recipient, index % 2 == 0 ? _recipient : _friend.UserId, new string('x', 200) + $" / letter {index}",
                DateTimeOffset.UtcNow.AddMinutes(-index), index % 2 == 0 ? DateTimeOffset.UtcNow : null, _friend)).ToArray();
            if (Page > 0 && !Empty)
            {
                // A repeated item must not create another row when a server page overlaps.
                items = [new LetterHistoryItem(_ids[start - 1], _friend.UserId, _recipient, "duplicate", DateTimeOffset.UtcNow, null, _friend), .. items];
            }
            Page++;
            var page = new LetterHistoryPage(items, Empty || Page == 20 ? null : Page.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(page) });
        }
    }

    private static void CheckHistoryVirtualization(string output, string language)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var handler = new HistoryPageHandler();
        var client = new OnlineClient(new Uri("https://example.test"), new OnlineMemoryStore(), handler);
        client.RestoreAsync().GetAwaiter().GetResult();
        var online = new OnlineCoordinator(client, action => action(), () => { });
        online.Publish(new AccountProfile(handler.OwnerId, "owner", null, null, null), [handler.Correspondent],
            new([], []), null, client.SessionId, true);
        var window = new AccountWindow(online);
        var conversation = (ConversationView)window.FindName("ConversationPage");
        ((SocialDashboard)window.FindName("DashboardPage")).Visibility = Visibility.Collapsed;
        conversation.Visibility = Visibility.Visible;
        try
        {
            Render(window, Path.Combine(output, $"history-{language}-empty.png"));
            Task initial = conversation.OpenAsync(handler.Correspondent);
            PumpUntil(() => initial.IsCompleted);
            initial.GetAwaiter().GetResult();
            var list = (ListBox)conversation.FindName("LettersList");
            Require(list.Items.Count == 50, "Conversation did not initially load only the latest page.");
            Render(window, Path.Combine(output, $"history-{language}-latest.png"));
            object anchor = list.Items[10];
            list.ScrollIntoView(anchor);
            list.UpdateLayout();
            PumpUntil(() => list.ItemContainerGenerator.ContainerFromItem(anchor) is not null);
            double anchorY = ((FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(anchor)).TranslatePoint(new Point(), list).Y;
            Task older = conversation.LoadOlderAsync();
            PumpUntil(() => older.IsCompleted);
            older.GetAwaiter().GetResult();
            list.UpdateLayout();
            Require(list.ItemContainerGenerator.ContainerFromItem(anchor) is FrameworkElement anchored &&
                Math.Abs(anchored.TranslatePoint(new Point(), list).Y - anchorY) < 2,
                "Prepending older letters moved the visible conversation anchor.");
            for (int page = 2; page < 20; page++)
            {
                Task more = conversation.LoadOlderAsync();
                PumpUntil(() => more.IsCompleted);
                more.GetAwaiter().GetResult();
            }
            Require(list.Items.Count == 1000, "Conversation pagination lost letters or introduced duplicates.");
            LetterHistoryItem[] chronological = list.Items.Cast<object>()
                .Select(item => (LetterHistoryItem)item.GetType().GetProperty("Letter")!.GetValue(item)!).ToArray();
            Require(chronological.Zip(chronological.Skip(1), (left, right) => left.SentAtUtc <= right.SentAtUtc).All(ordered => ordered),
                "Conversation pages were not merged in original sending order.");
            list.ScrollIntoView(list.Items[0]);
            Render(window, Path.Combine(output, $"history-{language}-virtualized-top.png"));
            int topRows = CheckRealizedHistoryRows(list);
            list.ScrollIntoView(list.Items[999]);
            ((FrameworkElement)window.Content).UpdateLayout();
            PumpUntil(() => list.ItemContainerGenerator.ContainerFromIndex(999) is not null);
            Render(window, Path.Combine(output, $"history-{language}-virtualized-bottom.png"));
            int bottomRows = CheckRealizedHistoryRows(list);
            Require(HasWrappedHistoryBody(list), "Conversation bubbles failed to wrap long letter bodies.");
            Require(list.ItemContainerGenerator.ContainerFromIndex(0) is null, "Conversation retained offscreen visual rows.");
            handler.Empty = true;
            Task empty = conversation.OpenAsync(handler.Correspondent);
            PumpUntil(() => empty.IsCompleted);
            empty.GetAwaiter().GetResult();
            Require(list.Items.Count == 0, "A new conversation retained old letters.");
            Console.WriteLine($"Conversation ({language}): 1000 unique letters; virtual rows {topRows}/{bottomRows}; paging, wrapping and reset passed.");
        }
        finally
        {
            window.Close();
            online.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
    private static int CheckRealizedHistoryRows(ListBox list)
    {
        int realized = Enumerable.Range(0, list.Items.Count).Count(index => list.ItemContainerGenerator.ContainerFromIndex(index) is not null);
        Require(realized is > 0 and < 40, $"History realized {realized} rows for {list.Items.Count} letters instead of only its viewport.");
        return realized;
    }

    private static bool HasWrappedHistoryBody(DependencyObject root)
    {
        if (root is TextBlock text && text.Text.Contains("/ letter 0", StringComparison.Ordinal))
        {
            return text.TextWrapping == TextWrapping.Wrap && text.ActualHeight > text.FontSize * 2;
        }
        return Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root))
            .Any(index => HasWrappedHistoryBody(VisualTreeHelper.GetChild(root, index)));
    }
}

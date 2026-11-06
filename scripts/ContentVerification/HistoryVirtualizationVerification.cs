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
            Require(request.RequestUri!.AbsolutePath == "/api/v1/letters/incoming", "Unexpected history request.");
            Require(request.RequestUri.Query.Contains($"cursor={(Empty || Page == 0 ? string.Empty : Page.ToString())}", StringComparison.Ordinal),
                "History pagination lost its server cursor.");
            int start = Page * 50;
            LetterHistoryItem[] items = Empty ? [] : Enumerable.Range(start, 50).Select(index => new LetterHistoryItem(
                _ids[index], _friend.UserId, _recipient, new string('x', 200) + $" / letter {index}",
                DateTimeOffset.UtcNow, index % 2 == 0 ? DateTimeOffset.UtcNow : null, _friend)).ToArray();
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
        var handler = new HistoryPageHandler();
        var client = new OnlineClient(new Uri("https://example.test"), new OnlineMemoryStore(), handler);
        client.RestoreAsync().GetAwaiter().GetResult();
        var online = new OnlineCoordinator(client, action => action(), () => { });
        var window = new LettersHistoryWindow(online, true);
        try
        {
            var list = (ListBox)window.FindName("LettersList");
            var more = (Button)window.FindName("MoreButton");
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            PumpUntil(() => ((UIElement)window.Content).IsEnabled);
            for (int page = 1; page < 20; page++)
            {
                Require(more.IsEnabled, "History stopped before its final cursor.");
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => ((UIElement)window.Content).IsEnabled);
            }
            Require(list.Items.Count == 1000 && !more.IsEnabled, "History pagination lost items, duplicates or the final cursor.");
            Render(window, Path.Combine(output, $"history-{language}-virtualized-top.png"));
            int topRows = CheckRealizedHistoryRows(list);
            Require(HasWrappedHistoryBody(list), "A long history body was clipped instead of wrapping within its row.");
            list.ScrollIntoView(list.Items[999]);
            ((FrameworkElement)window.Content).UpdateLayout();
            PumpUntil(() => list.ItemContainerGenerator.ContainerFromIndex(999) is not null);
            Render(window, Path.Combine(output, $"history-{language}-virtualized-bottom.png"));
            int bottomRows = CheckRealizedHistoryRows(list);
            Require(list.ItemContainerGenerator.ContainerFromIndex(0) is null, "History retained its first visual row after scrolling to the end.");
            handler.Empty = true;
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            PumpUntil(() => ((UIElement)window.Content).IsEnabled);
            Require(list.Items.Count == 0 && !more.IsEnabled, "Refreshing empty history retained old letters or its cursor.");
            Console.WriteLine($"History virtualization ({language}): 1000 unique letters; realized rows top={topRows}, bottom={bottomRows}; scrolling and empty reset passed.");
        }
        finally
        {
            window.Close();
            online.DisposeAsync().AsTask().GetAwaiter().GetResult();
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

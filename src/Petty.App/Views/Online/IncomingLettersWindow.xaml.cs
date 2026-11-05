using System.Windows;
using System.Windows.Controls;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.App.Views.Online;

public partial class IncomingLettersWindow : OnlineWindow
{
    private readonly IncomingVisit _visit;
    private readonly Dictionary<Guid, TextBlock> _states = [];
    internal event Action? AllRead;

    internal IncomingLettersWindow(OnlineCoordinator online, IncomingVisit visit)
    {
        InitializeComponent();
        InitializeOnline(online);
        _visit = visit;
        SenderText.Text = visit.Snapshot.Sender.Login;
        int number = 0;
        foreach (DeliveryLetter letter in visit.Snapshot.Letters.OrderBy(letter => letter.SentAtUtc).ThenBy(letter => letter.Id))
        {
            var state = new TextBlock();
            state.SetResourceReference(TextBlock.TextProperty, visit.IsRead(letter.Id) ? "OnlineRead" : "OnlineUnread");
            _states.Add(letter.Id, state);
            var row = new StackPanel();
            row.Children.Add(state);
            row.Children.Add(new TextBlock { Text = letter.Body, Margin = new Thickness(0, 8, 0, 0) });
            var expander = new Expander { Header = $"{++number} · {letter.SentAtUtc.ToLocalTime():g}", Content = row };
            expander.Expanded += async (_, _) => await AcknowledgeAsync([letter.Id]);
            // Explicit retry also works when the body is already expanded after an offline failure.
            var retry = new Button();
            retry.SetResourceReference(ContentControl.ContentProperty, "OnlineConfirmRead");
            retry.Click += async (_, _) => await AcknowledgeAsync([letter.Id]);
            row.Children.Add(retry);
            LettersPanel.Children.Add(new Border { Child = expander, Style = (Style)FindResource("OnlineCard") });
        }
    }

    private async void OnReadAll(object sender, RoutedEventArgs e) => await AcknowledgeAsync(_visit.UnreadIds);

    private async Task AcknowledgeAsync(Guid[] ids)
    {
        await RunAsync(async () =>
        {
            await _visit.ReadAsync(Online.Client, ids, Lifetime);
            foreach (KeyValuePair<Guid, TextBlock> pair in _states)
            {
                pair.Value.SetResourceReference(TextBlock.TextProperty, _visit.IsRead(pair.Key) ? "OnlineRead" : "OnlineUnread");
            }
            if (_visit.Complete)
            {
                AllRead?.Invoke();
                Close();
            }
        });
    }
}

using System.Windows;
using System.Windows.Controls;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.App.Views.Online;

public partial class LettersHistoryWindow : OnlineWindow
{
    private readonly bool _incoming;
    private string? _cursor;
    private readonly HashSet<Guid> _shown = [];

    internal LettersHistoryWindow(OnlineCoordinator online, bool incoming)
    {
        InitializeComponent();
        InitializeOnline(online);
        _incoming = incoming;
        SetResourceReference(TitleProperty, incoming ? "OnlineReceived" : "OnlineSent");
        HeadingText.SetResourceReference(TextBlock.TextProperty, incoming ? "OnlineReceived" : "OnlineSent");
        Loaded += OnRefresh;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RunAsync(() => LoadAsync(true));
    private async void OnMore(object sender, RoutedEventArgs e) => await RunAsync(() => LoadAsync(false));
    protected override void UpdateAvailability() => MoreButton.IsEnabled = !Busy && _cursor is not null;

    private async Task LoadAsync(bool reset)
    {
        LetterHistoryPage page = await Online.Client.GetHistoryAsync(_incoming, reset ? null : _cursor, ct: Lifetime);
        Lifetime.ThrowIfCancellationRequested();
        if (reset)
        {
            LettersPanel.Children.Clear();
            _shown.Clear();
        }
        foreach (LetterHistoryItem letter in page.Items.Where(letter => _shown.Add(letter.Id)))
        {
            var row = new StackPanel();
            row.Children.Add(new TextBlock { Text = $"{letter.OtherUser.Login} · {letter.SentAtUtc.ToLocalTime():g}", FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = letter.Body, Margin = new Thickness(0, 8, 0, 8) });
            var read = new TextBlock();
            if (letter.ReadAtUtc is { } at)
            {
                read.Text = string.Format(Localization.Text("OnlineReadAtFormat"), at.ToLocalTime());
            }
            else
            {
                read.SetResourceReference(TextBlock.TextProperty, "OnlineUnread");
            }
            row.Children.Add(read);
            LettersPanel.Children.Add(new Border { Child = row, Style = (Style)FindResource("OnlineCard") });
        }
        _cursor = page.NextCursor;
        if (_shown.Count == 0)
        {
            var empty = new TextBlock();
            empty.SetResourceReference(TextBlock.TextProperty, "OnlineEmpty");
            LettersPanel.Children.Add(empty);
        }
    }
}

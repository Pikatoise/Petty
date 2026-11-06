using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<HistoryRow> _letters = [];

    internal LettersHistoryWindow(OnlineCoordinator online, bool incoming)
    {
        InitializeComponent();
        LettersList.ItemsSource = _letters;
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
            _letters.Clear();
            _shown.Clear();
        }
        foreach (LetterHistoryItem letter in page.Items.Where(letter => _shown.Add(letter.Id)))
        {
            _letters.Add(new HistoryRow($"{letter.OtherUser.Login} · {letter.SentAtUtc.ToLocalTime():g}", letter.Body,
                letter.ReadAtUtc is { } at ? string.Format(Localization.Text("OnlineReadAtFormat"), at.ToLocalTime()) : null));
        }
        _cursor = page.NextCursor;
    }

    private sealed record HistoryRow(string Heading, string Body, string? ReadAtText);
}

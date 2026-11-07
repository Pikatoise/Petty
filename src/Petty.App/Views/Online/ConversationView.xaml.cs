using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.App.Views.Online;

public partial class ConversationView : UserControl
{
    private OnlineCoordinator _online = null!;
    private Func<Func<Task>, Task<bool>> _run = null!;
    private CancellationToken _lifetime;
    private readonly ObservableCollection<MessageRow> _letters = [];
    private readonly HashSet<Guid> _shown = [];
    private Friend? _friend;
    private string? _cursor;
    private bool _loading;
    private bool _initial = true;
    private int _generation;
    public event Action? Back;
    internal Guid? CorrespondentId => _friend?.UserId;
    public ConversationView()
    {
        InitializeComponent();
        LettersList.ItemsSource = _letters;
    }
    internal void Initialize(OnlineCoordinator online, Func<Func<Task>, Task<bool>> run, CancellationToken lifetime)
    {
        _online = online;
        _run = run;
        _lifetime = lifetime;
    }
    internal void Reset()
    {
        _generation++;
        _friend = null;
        _cursor = null;
        _initial = true;
        _letters.Clear();
        _shown.Clear();
        FriendLogin.Text = string.Empty;
        RetryButton.Visibility = EmptyText.Visibility = Visibility.Collapsed;
    }
    internal async Task OpenAsync(Friend friend)
    {
        Reset();
        _friend = friend;
        FriendLogin.Text = friend.Login;
        await LoadOlderAsync();
    }
    internal async Task LoadOlderAsync()
    {
        if (_loading || _friend is null || (!_initial && _cursor is null))
        {
            return;
        }
        int generation = _generation;
        Guid? session = _online.Client.SessionId;
        Guid friendId = _friend.UserId;
        ScrollViewer? scroll = FindScroll(LettersList);
        MessageRow? anchor = null;
        double anchorY = 0;
        if (!_initial && scroll is not null)
        {
            foreach (MessageRow row in _letters)
            {
                if (LettersList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem container)
                {
                    double y = container.TranslatePoint(new Point(), scroll).Y;
                    if (y + container.ActualHeight > 0)
                    {
                        anchor = row;
                        anchorY = y;
                        break;
                    }
                }
            }
        }
        _loading = true;
        bool success = false;
        try
        {
            success = await _run(async () =>
            {
                LetterHistoryPage page = await _online.Client.GetConversationAsync(friendId, _cursor, ct: _lifetime);
                if (generation != _generation || session != _online.Client.SessionId || _lifetime.IsCancellationRequested)
                {
                    return;
                }
                foreach (LetterHistoryItem letter in page.Items.Where(l => _shown.Add(l.Id)))
                {
                    _letters.Insert(0, new MessageRow(letter, letter.SenderId == _online.Account?.UserId));
                }
                _cursor = page.NextCursor;
                bool first = _initial;
                _initial = false;
                EmptyText.Visibility = _letters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                await Dispatcher.InvokeAsync(() =>
                {
                    if (generation != _generation)
                    {
                        return;
                    }
                    LettersList.UpdateLayout();
                    ScrollViewer? current = FindScroll(LettersList);
                    if (first)
                    {
                        current?.ScrollToBottom();
                    }
                    else if (anchor is not null && current is not null)
                    {
                        LettersList.ScrollIntoView(anchor);
                        LettersList.UpdateLayout();
                        if (LettersList.ItemContainerGenerator.ContainerFromItem(anchor) is ListBoxItem container)
                        {
                            current.ScrollToVerticalOffset(current.VerticalOffset + container.TranslatePoint(new Point(), current).Y - anchorY);
                        }
                    }
                }, DispatcherPriority.Loaded);
            });
        }
        finally
        {
            _loading = false;
            if (generation == _generation)
            {
                RetryButton.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }
    private void OnBack(object sender, RoutedEventArgs e) => Back?.Invoke();
    private async void OnRetry(object sender, RoutedEventArgs e) => await LoadOlderAsync();
    private async void OnScroll(object sender, ScrollChangedEventArgs e)
    {
        if (IsVisible && e.VerticalChange < 0 && e.VerticalOffset < 32)
        {
            await LoadOlderAsync();
        }
    }
    private async void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0 && FindScroll(LettersList)?.VerticalOffset < 32)
        {
            await LoadOlderAsync();
        }
    }
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll)
        {
            return scroll;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } child)
            {
                return child;
            }
        }
        return null;
    }
    private sealed record MessageRow(LetterHistoryItem Letter, bool Outgoing)
    {
        public string Body => Letter.Body;
        public string Timestamp => Letter.SentAtUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        public string ReadStatus => Letter.ReadAtUtc is { } read
            ? string.Format(Localization.Text("OnlineReadAtFormat"), read.ToLocalTime()) : Localization.Text("OnlineUnread");
    }
}

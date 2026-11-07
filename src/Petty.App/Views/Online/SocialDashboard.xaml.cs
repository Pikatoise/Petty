using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.App.Views.Online;

public partial class SocialDashboard : UserControl, IDisposable
{
    private OnlineCoordinator _online = null!;
    private Func<Func<Task>, Task<bool>> _run = null!;
    private CancellationToken _lifetime;
    private Row? _confirmation;
    private bool _blocking;
    private IInputElement? _returnFocus;
    public event Action<Friend>? OpenConversation;
    public event RoutedEventHandler? Logout;

    public SocialDashboard() => InitializeComponent();

    internal void Initialize(OnlineCoordinator online, Func<Func<Task>, Task<bool>> run, CancellationToken lifetime)
    {
        _online = online;
        _run = run;
        _lifetime = lifetime;
        online.FriendsChanged += RenderFriends;
        online.AccountChanged += RenderFriends;
        online.FriendsRefreshCompleted += RefreshCompleted;
        RenderFriends();
    }

    private void RefreshCompleted(bool success)
    {
        if (_online.Client.SessionId is null)
        {
            return;
        }
        LoadingText.Visibility = Visibility.Collapsed;
        if (!success)
        {
            Feedback("OnlineRequestFailed");
        }
    }

    private void Feedback(string key) => ((AccountWindow)Window.GetWindow(this))?.ShowFeedback(key);

    internal void Reset()
    {
        CloseModal();
        FriendLogin.Clear();
        UserLogin.SetResourceReference(TextBlock.TextProperty, "OnlineWorking");
        LoadingText.Visibility = Visibility.Visible;
        foreach (ItemsControl list in new[] { FriendsList, IncomingList, OutgoingList, BlockedList })
        {
            list.ItemsSource = null;
        }
        FriendsSection.IsExpanded = true;
        IncomingSection.IsExpanded = OutgoingSection.IsExpanded = BlockedSection.IsExpanded = false;
        DashboardScroll.ScrollToTop();
    }

    private void RenderFriends()
    {
        if (_online.Client.SessionId is null || _online.PublishedSessionId != _online.Client.SessionId)
        {
            return;
        }
        UserLogin.Text = _online.Account?.Login ?? Localization.Text("OnlineWorking");
        LoadingText.Visibility = Visibility.Collapsed;
        SetRows(FriendsList, _online.Friends.Select(f => new Row(f.UserId, f.Login)));
        SetRows(IncomingList, _online.Requests.Incoming.Select(r => new Row(r.User.UserId, r.User.Login, r.RequestId)));
        SetRows(OutgoingList, _online.Requests.Outgoing.Select(r => new Row(r.User.UserId, r.User.Login, r.RequestId)));
        SetRows(BlockedList, _online.BlockedUsers.Select(u => new Row(u.UserId, u.Login)));
    }

    internal void RefreshSnapshot() => RenderFriends();
    internal double ScrollOffset
    {
        get => DashboardScroll.VerticalOffset;
        set => DashboardScroll.ScrollToVerticalOffset(value);
    }

    private static void SetRows(ItemsControl list, IEnumerable<Row> rows)
    {
        Row[] next = rows.ToArray();
        if (!list.Items.Cast<Row>().SequenceEqual(next))
        {
            list.ItemsSource = next;
        }
    }

    private static Row Selected(object sender) => (Row)((Button)sender).Tag;
    private void OnHistory(object sender, RoutedEventArgs e)
    {
        Row row = Selected(sender);
        OpenConversation?.Invoke(new Friend(row.UserId, row.Login, null, null));
    }

    private void OnLogout(object sender, RoutedEventArgs e) => Logout?.Invoke(sender, e);
    private void OnShowAdd(object sender, RoutedEventArgs e)
    {
        ShowModal(true);
        FriendLogin.Focus();
    }

    private void ShowModal(bool add)
    {
        _returnFocus = Keyboard.FocusedElement;
        Modal.Visibility = Visibility.Visible;
        DashboardScroll.IsEnabled = false;
        AddForm.Visibility = add ? Visibility.Visible : Visibility.Collapsed;
        ConfirmationForm.Visibility = add ? Visibility.Collapsed : Visibility.Visible;
        AddButton.IsDefault = add;
        ConfirmButton.IsDefault = !add;
    }

    private void CloseModal()
    {
        Modal.Visibility = Visibility.Collapsed;
        DashboardScroll.IsEnabled = true;
        AddButton.IsDefault = ConfirmButton.IsDefault = false;
        _confirmation = null;
        _returnFocus?.Focus();
        _returnFocus = null;
    }
    private void OnCloseModal(object sender, RoutedEventArgs e) => CloseModal();
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Modal.Visibility == Visibility.Visible)
        {
            CloseModal();
            e.Handled = true;
        }
    }

    private void Confirm(Row row, bool block)
    {
        ShowModal(false);
        _confirmation = row;
        _blocking = block;
        ConfirmationText.Text = string.Format(Localization.Text(block ? "OnlineConfirmBlock" : "OnlineConfirmRemove"), row.Login);
        ConfirmButton.SetResourceReference(ContentControl.ContentProperty, block ? "OnlineBlock" : "OnlineRemove");
        ConfirmButton.Focus();
    }
    private void OnBlock(object sender, RoutedEventArgs e) => Confirm(Selected(sender), true);
    private void OnRemove(object sender, RoutedEventArgs e) => Confirm(Selected(sender), false);

    private async Task MutateAsync(Func<Task> operation)
    {
        Guid? session = _online.Client.SessionId;
        if (session is null)
        {
            return;
        }
        bool success = await _run(operation);
        if (success && session == _online.Client.SessionId)
        {
            CloseModal();
            _online.RefreshFriends();
            LoadingText.Visibility = Visibility.Visible;
            Feedback("OnlineDone");
        }
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        string login = FriendLogin.Text.Trim();
        if (login.Length == 0)
        {
            FriendLogin.Focus();
            return;
        }
        await MutateAsync(() => _online.Client.RequestFriendAsync(login, _lifetime));
    }
    private async void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (_confirmation is not { } row)
        {
            return;
        }
        await MutateAsync(() => _blocking ? _online.Client.SetBlockAsync(row.UserId, true, _lifetime)
            : _online.Client.RemoveFriendAsync(row.UserId, _lifetime));
    }
    private async void OnAccept(object sender, RoutedEventArgs e) =>
        await MutateAsync(() => _online.Client.RespondToFriendRequestAsync(Selected(sender).RequestId!.Value, true, _lifetime));
    private async void OnDecline(object sender, RoutedEventArgs e) =>
        await MutateAsync(() => _online.Client.RespondToFriendRequestAsync(Selected(sender).RequestId!.Value, false, _lifetime));
    private async void OnCancel(object sender, RoutedEventArgs e) =>
        await MutateAsync(() => _online.Client.CancelFriendRequestAsync(Selected(sender).RequestId!.Value, _lifetime));
    private async void OnUnblock(object sender, RoutedEventArgs e) =>
        await MutateAsync(() => _online.Client.SetBlockAsync(Selected(sender).UserId, false, _lifetime));

    public void Dispose()
    {
        _online.FriendsChanged -= RenderFriends;
        _online.AccountChanged -= RenderFriends;
        _online.FriendsRefreshCompleted -= RefreshCompleted;
    }

    private sealed record Row(Guid UserId, string Login, Guid? RequestId = null);
}

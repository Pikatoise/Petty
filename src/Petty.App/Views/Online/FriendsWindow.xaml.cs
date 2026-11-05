using System.Windows;
using System.Windows.Controls;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.App.Views.Online;

public partial class FriendsWindow : OnlineWindow
{
    private readonly OnlineWindows _windows;

    internal FriendsWindow(OnlineCoordinator online, OnlineWindows windows)
    {
        _windows = windows;
        InitializeComponent();
        InitializeOnline(online);
        Loaded += OnRefresh;
        online.AccountChanged += OnAccountChanged;
        Closed += (_, _) => online.AccountChanged -= OnAccountChanged;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RunAsync(LoadAsync);
    private void OnAccountChanged() => _ = RunAsync(LoadAsync);

    private async Task LoadAsync()
    {
        Friend[] friends = await Online.Client.GetFriendsAsync(Lifetime);
        FriendRequests requests = await Online.Client.GetFriendRequestsAsync(Lifetime);
        Lifetime.ThrowIfCancellationRequested();
        FriendsPanel.Children.Clear();
        IncomingPanel.Children.Clear();
        OutgoingPanel.Children.Clear();
        foreach (Friend friend in friends)
        {
            StackPanel row = AddRow(FriendsPanel, friend);
            AddAction(row, "OnlineCompose", () => { _windows.ShowCompose(friend.UserId); return Task.CompletedTask; });
            AddAction(row, "OnlineRemove", () => Online.Client.RemoveFriendAsync(friend.UserId, Lifetime));
            AddAction(row, "OnlineBlock", () => Online.Client.SetBlockAsync(friend.UserId, true, Lifetime));
        }
        foreach (FriendRequest request in requests.Incoming)
        {
            StackPanel row = AddRow(IncomingPanel, request.User);
            AddAction(row, "OnlineAccept", () => Online.Client.RespondToFriendRequestAsync(request.RequestId, true, Lifetime));
            AddAction(row, "OnlineDecline", () => Online.Client.RespondToFriendRequestAsync(request.RequestId, false, Lifetime));
            AddAction(row, "OnlineBlock", () => Online.Client.SetBlockAsync(request.User.UserId, true, Lifetime));
        }
        foreach (FriendRequest request in requests.Outgoing)
        {
            AddRow(OutgoingPanel, request.User);
        }
        foreach (StackPanel panel in new[] { FriendsPanel, IncomingPanel, OutgoingPanel })
        {
            if (panel.Children.Count == 0)
            {
                var empty = new TextBlock();
                empty.SetResourceReference(TextBlock.TextProperty, "OnlineEmpty");
                panel.Children.Add(empty);
            }
        }
    }

    private StackPanel AddRow(Panel parent, Friend friend)
    {
        var row = new StackPanel();
        row.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(friend.PetName) ? friend.Login : $"{friend.Login} · {friend.PetName}" });
        var border = new Border { Child = row, Style = (Style)FindResource("OnlineCard") };
        parent.Children.Add(border);
        return row;
    }

    private void AddAction(Panel row, string key, Func<Task> action)
    {
        var button = new Button { HorizontalAlignment = HorizontalAlignment.Left };
        button.SetResourceReference(ContentControl.ContentProperty, key);
        button.Click += async (_, _) => await RunAsync(async () =>
        {
            await action();
            await LoadAsync();
            Online.Refresh();
        });
        row.Children.Add(button);
    }

    private async void OnAdd(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await Online.Client.RequestFriendAsync(LoginBox.Text.Trim(), Lifetime);
        LoginBox.Clear();
        await LoadAsync();
        Online.Refresh();
    });
}

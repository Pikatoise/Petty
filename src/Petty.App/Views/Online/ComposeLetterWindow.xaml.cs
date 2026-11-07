using System.Windows;
using System.Windows.Controls;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.App.Views.Online;

public partial class ComposeLetterWindow : OnlineWindow
{
    private readonly LetterDraft _draft = new();
    private MailQuota? _quota;
    private Guid? _quotaRecipient;
    private bool _ready;

    internal ComposeLetterWindow(OnlineCoordinator online, Guid? recipient = null)
    {
        InitializeComponent();
        InitializeOnline(online);
        _ready = true;
        Loaded += async (_, _) => await RunAsync(() => LoadRecipientsAsync(recipient));
    }

    private async Task LoadRecipientsAsync(Guid? recipient)
    {
        if (_draft.Pending is null)
        {
            Friend[] friends = await Online.Client.GetFriendsAsync(Lifetime);
            _ready = false;
            FriendSelector.ItemsSource = friends;
            FriendSelector.SelectedValue = recipient;
            if (FriendSelector.SelectedIndex < 0 && FriendSelector.Items.Count > 0)
            {
                FriendSelector.SelectedIndex = 0;
            }
            _ready = true;
        }
        await LoadQuotaAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) =>
        await RunAsync(() => LoadRecipientsAsync((FriendSelector.SelectedItem as Friend)?.UserId));

    private async Task LoadQuotaAsync()
    {
        _quota = null;
        _quotaRecipient = null;
        if (FriendSelector.SelectedItem is Friend friend)
        {
            _quota = await Online.Client.GetQuotaAsync(friend.UserId, Lifetime);
            _quotaRecipient = friend.UserId;
            QuotaText.Text = string.Format(Localization.Text("OnlineQuotaFormat"), _quota.UnreadCount, _quota.Limit);
        }
        else
        {
            QuotaText.SetResourceReference(TextBlock.TextProperty, "OnlineNoFriends");
        }
    }

    private async void OnRecipientChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ready)
        {
            await RunAsync(LoadQuotaAsync);
        }
    }

    private void OnBodyChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
        {
            UpdateAvailability();
        }
    }

    protected override void UpdateAvailability()
    {
        if (!_ready)
        {
            return;
        }
        bool frozen = _draft.Pending is not null;
        FriendSelector.IsEnabled = !frozen;
        BodyBox.IsReadOnly = frozen;
        RetryText.Visibility = frozen && !_draft.Sent ? Visibility.Visible : Visibility.Collapsed;
        SendButton.SetResourceReference(ContentControl.ContentProperty, frozen && !_draft.Sent ? "OnlineRetry" : "OnlineSend");
        // A lost success response can leave quota at 10: an idempotent replay must still be possible.
        SendButton.IsEnabled = !Busy && !_draft.Sent && (frozen ||
            (FriendSelector.SelectedItem is Friend friend && _quotaRecipient == friend.UserId &&
             _quota is not null && _quota.UnreadCount < _quota.Limit && LetterDraft.IsValidBody(BodyBox.Text)));
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        bool sent = false;
        await RunAsync(async () =>
        {
            if (_draft.Pending is null)
            {
                await LoadQuotaAsync();
                if (_quota is null || _quota.UnreadCount >= _quota.Limit)
                {
                    throw new OnlineException(System.Net.HttpStatusCode.Conflict, "UNREAD_MAIL_LIMIT_REACHED");
                }
            }
            SendLetter request = _draft.Pending ?? _draft.Prepare(((Friend)FriendSelector.SelectedItem).UserId, BodyBox.Text);
            SentLetter result;
            try
            {
                result = await Online.Client.SendLetterAsync(request, Lifetime);
            }
            catch (OnlineException ex) when (ex.Code is "UNREAD_MAIL_LIMIT_REACHED" or "FRIENDSHIP_REQUIRED")
            {
                _draft.Reject();
                _quota = ex.UnreadCount is { } count && ex.Limit is { } limit ? new MailQuota(count, limit) : null;
                if (_quota is not null)
                {
                    QuotaText.Text = string.Format(Localization.Text("OnlineQuotaFormat"), _quota.UnreadCount, _quota.Limit);
                }
                throw;
            }
            Lifetime.ThrowIfCancellationRequested();
            _draft.Confirm(result);
            QuotaText.Text = string.Format(Localization.Text("OnlineQuotaFormat"), result.UnreadCount, result.Limit);
            sent = true;
        });
        if (sent && !IsClosed)
        {
            Status("OnlineLetterSent");
        }
    }
}

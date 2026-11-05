using System.Windows;
using Petty.App.Online;

namespace Petty.App.Views.Online;

/// <summary>Owns modeless social windows separately from the local pet session.</summary>
internal sealed class OnlineWindows : IDisposable
{
    private readonly OnlineCoordinator _online;
    private readonly Action<bool> _letterAvailability;
    private readonly Dictionary<string, Window> _windows = [];
    private bool _disposed;
#if DEBUG
    private IncomingVisit? _visit;
    private Guid? _visitSession;
#endif

    public OnlineWindows(OnlineCoordinator online, Action<bool> letterAvailability)
    {
        _online = online;
        _letterAvailability = letterAvailability;
        online.AccountChanged += AccountChanged;
    }

    public void ShowAccount() => Show("account", () => new AccountWindow(_online, this));
    public void ShowFriends() => Authenticated(() => Show("friends", () => new FriendsWindow(_online, this)));
    public void ShowCompose(Guid? recipient = null) => Authenticated(() => Show("compose", () => new ComposeLetterWindow(_online, recipient)));
    public void ShowHistory(bool incoming) => Authenticated(() => Show(incoming ? "received" : "sent", () => new LettersHistoryWindow(_online, incoming)));

    private void Authenticated(Action action)
    {
        if (_online.Client.SessionId is null)
        {
            ShowAccount();
            return;
        }
        action();
    }

    private void Show(string key, Func<Window> create)
    {
        if (_disposed)
        {
            return;
        }
        if (!_windows.TryGetValue(key, out Window? window))
        {
            window = create();
            _windows.Add(key, window);
            window.Closed += (_, _) => _windows.Remove(key);
            window.Show();
        }
        window.Activate();
    }

    private void AccountChanged()
    {
        _letterAvailability(_online.Account is not null && _online.Client.SessionId is not null &&
            _online.PublishedSessionId == _online.Client.SessionId);
#if DEBUG
        if (_visit is not null && (_online.Client.SessionId != _visitSession ||
            !_online.Friends.Any(friend => friend.UserId == _visit.Snapshot.Sender.UserId)))
        {
            _windows.GetValueOrDefault("incoming")?.Close();
            _visit = null;
        }
#endif
    }

#if DEBUG
    // Temporary C2 verification entry. Release delivery is owned by the D2 postman.
    public void ShowIncoming() => Authenticated(() =>
    {
        if (_online.PublishedSessionId != _online.Client.SessionId)
        {
            _online.Refresh();
            return;
        }
        if (_visitSession != _online.Client.SessionId)
        {
            _visit = null;
        }
        if (_visit is null && _online.Mailbox is { } mailbox)
        {
            _visit = new IncomingVisit(mailbox);
            _visitSession = _online.Client.SessionId;
        }
        if (_visit is null)
        {
            MessageBox.Show(Localization.Text("OnlineEmpty"), Localization.Text("OnlineIncomingLetters"));
            return;
        }
        Show("incoming", () =>
        {
            var window = new IncomingLettersWindow(_online, _visit);
            window.AllRead += () =>
            {
                _visit = null;
                _online.VisitCompleted();
            };
            return window;
        });
    });
#endif

    public void Dispose()
    {
        _disposed = true;
        _online.AccountChanged -= AccountChanged;
        foreach (Window window in _windows.Values.ToArray())
        {
            window.Close();
        }
        _windows.Clear();
    }
}

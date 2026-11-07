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

    public OnlineWindows(OnlineCoordinator online, Action<bool> letterAvailability)
    {
        _online = online;
        _letterAvailability = letterAvailability;
        online.AccountChanged += AccountChanged;
    }

    public void ShowAccount() => Show("account", () => new AccountWindow(_online));
    public void ShowCompose(Guid? recipient = null) => Authenticated(() => Show("compose", () => new ComposeLetterWindow(_online, recipient)));

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
    }

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

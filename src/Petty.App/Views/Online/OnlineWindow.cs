using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Petty.App.Online;
using Petty.Online.Client;

namespace Petty.App.Views.Online;

/// <summary>Shared cancellation, safe errors and account isolation for online windows.</summary>
public class OnlineWindow : Window
{
    private readonly CancellationTokenSource _closed = new();
    private OnlineCoordinator? _online;
    private Guid? _session;
    private bool _requiresAccount;
    protected bool Busy
    {
        get; private set;
    }
    protected bool IsClosed => _closed.IsCancellationRequested;
    protected CancellationToken Lifetime => _closed.Token;
    private protected OnlineCoordinator Online => _online!;

    protected OnlineWindow()
    {
        Width = 540;
        Height = 610;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        SetResourceReference(BackgroundProperty, "CozyBackground");
        SetResourceReference(ForegroundProperty, "CozyText");
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Petty;component/Resources/OnlineControls.xaml", UriKind.Relative)
        });
        Closed += (_, _) =>
        {
            _closed.Cancel();
            if (_online is not null)
            {
                _online.Client.SessionChanged -= SessionChanged;
            }
        };
    }

    private protected void InitializeOnline(OnlineCoordinator online, bool requiresAccount = true)
    {
        AdaptiveWindowLayout.Attach(this);
        _online = online;
        _requiresAccount = requiresAccount;
        _session = online.Client.SessionId;
        online.Client.SessionChanged += SessionChanged;
    }

    private void SessionChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (!IsClosed && _requiresAccount && _session != Online.Client.SessionId)
        {
            Close();
        }
    });

    protected void Status(string key) => ((TextBlock)FindName("StatusText")).SetResourceReference(TextBlock.TextProperty, key);

    protected async Task<bool> RunAsync(Func<Task> operation)
    {
        if (Busy || IsClosed)
        {
            return false;
        }
        Busy = true;
        ((UIElement)Content).IsEnabled = false;
        Status("OnlineWorking");
        try
        {
            if (_requiresAccount && (_session is null || _session != Online.Client.SessionId))
            {
                Close();
                return false;
            }
            await operation();
            if (!IsClosed)
            {
                ((TextBlock)FindName("StatusText")).Text = string.Empty;
            }
            return true;
        }
        catch (OperationCanceledException) when (IsClosed) { return false; }
        catch (Exception ex)
        {
            if (!IsClosed)
            {
                Status(ErrorKey(ex));
            }
            return false;
        }
        finally
        {
            Busy = false;
            ((UIElement)Content).IsEnabled = true;
            UpdateAvailability();
        }
    }

    protected virtual void UpdateAvailability()
    {
    }

    private static string ErrorKey(Exception exception) => exception is OnlineException error ? error.Code switch
    {
        "INVALID_CREDENTIALS" => "OnlineInvalidCredentials",
        "INVALID_LOGIN" or "INVALID_REGISTRATION" or "PASSWORD_REJECTED" => "OnlineRegistrationInvalid",
        "USERNAME_TAKEN" => "OnlineLoginTaken",
        "LOGIN_REQUIRED" or "SESSION_REVOKED" or "INVALID_REFRESH_TOKEN" => "OnlineSessionEnded",
        "UNREAD_MAIL_LIMIT_REACHED" => "OnlineQuotaReached",
        "FRIENDSHIP_REQUIRED" or "LETTER_NOT_AVAILABLE" => "OnlineFriendRequired",
        "USER_NOT_AVAILABLE" => "OnlineUserUnavailable",
        "FRIEND_REQUEST_EXISTS" or "FRIENDSHIP_ALREADY_EXISTS" => "OnlineFriendExists",
        "INVALID_LETTER" => "OnlineLetterInvalid",
        _ => "OnlineRequestFailed"
    } : "OnlineRequestFailed";
}

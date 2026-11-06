using System.IO;
using System.Windows.Threading;
using Petty.App.Desktop;
using Petty.App.Online;
using Petty.App.Sprites;
using Petty.App.Views.Online;
using Petty.Core.Assets;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Online.Client;

namespace Petty.App.Delivery;

/// <summary>One guest and one immutable incoming snapshot; REST selects each subsequent visit.</summary>
internal sealed class PostmanCoordinator : IDisposable
{
    private readonly OnlineCoordinator _online;
    private readonly DisplayService _displays;
    private readonly DesktopLayerMode _layer;
    private readonly IAppLog _log;
    private readonly Func<IReadOnlyList<ScreenRect>> _obstacles;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _fetchStop;
    private VisitorSession? _visitor;
    private IncomingVisit? _visit;
    private IncomingLettersWindow? _letters;
    private Guid? _session;
    private bool _homeReady;
    private bool _fetching;
    private bool _refreshPending;
    private bool _disposed;

    public PostmanCoordinator(OnlineCoordinator online, DisplayService displays, DesktopLayerMode layer,
        IAppLog log, Func<IReadOnlyList<ScreenRect>> obstacles)
    {
        _online = online;
        _displays = displays;
        _layer = layer;
        _log = log;
        _obstacles = obstacles;
        online.MailboxChanged += Published;
        online.Client.SessionChanged += SessionChanged;
    }

    public void SetHomeReady(bool ready)
    {
        if (_disposed)
        {
            return;
        }
        _homeReady = ready;
        if (!ready)
        {
            _fetchStop?.Cancel();
            CloseVisit();
        }
        else
        {
            Published();
        }
    }

    public void RestoreShell() => _visitor?.RestoreShell();

    private void SessionChanged() => _dispatcher.BeginInvoke(() =>
    {
        if (_disposed)
        {
            return;
        }
        if (_session != _online.Client.SessionId)
        {
            _fetchStop?.Cancel();
            CloseVisit();
        }
        Published();
    });

    private void Published()
    {
        if (_disposed || !_homeReady)
        {
            return;
        }
        if (_visitor is not null)
        {
            if (_session != _online.Client.SessionId)
            {
                CloseVisit();
            }
            else
            {
                if (_online.PublishedSessionId == _session &&
                    !_online.Friends.Any(friend => friend.UserId == _visit!.Snapshot.Sender.UserId))
                {
                    CloseLetters();
                    _visitor.RequestExit();
                }
                return;
            }
        }
        if (_fetching)
        {
            _refreshPending = true;
            return;
        }
        _ = FetchVisitAsync();
    }

    private async Task FetchVisitAsync()
    {
        Guid? session = _online.Client.SessionId;
        if (_disposed || !_homeReady || session is null || _online.PublishedSessionId != session || _online.Account is null)
        {
            return;
        }
        _fetching = true;
        _refreshPending = false;
        using var fetch = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _fetchStop = fetch;
        try
        {
            // Never consume the coordinator's cached candidate after a guest has exited.
            MailboxVisit? snapshot = await _online.Client.GetNextVisitAsync(fetch.Token);
            fetch.Token.ThrowIfCancellationRequested();
            if (_disposed || !_homeReady || _online.Client.SessionId != session || _visitor is not null || snapshot is null || snapshot.Letters.Count == 0)
            {
                return;
            }
            if (!_online.Friends.Any(friend => friend.UserId == snapshot.Sender.UserId))
            {
                _online.Refresh();
                return;
            }
            _session = session;
            _visit = new IncomingVisit(snapshot);
            _visitor = new VisitorSession(LoadMailSheet(snapshot.Sender.PetAssetId), _displays, _layer, _log, _obstacles);
            _visitor.LettersRequested += OpenLetters;
            _visitor.Finished += Finished;
            _visitor.Start();
        }
        catch (OperationCanceledException) when (fetch.IsCancellationRequested) { }
        catch (Exception)
        {
            CloseVisit();
            _log.Warn("Postman unavailable; unread mail stays on the server.");
        }
        finally
        {
            _fetchStop = null;
            _fetching = false;
            if (_refreshPending && !_disposed && _homeReady)
            {
                Published();
            }
        }
    }

    internal static SpriteSheet LoadMailSheet(string? appearance)
    {
        const string fallback = PetCatalog.DefaultAssetId;
        string id = SpriteMetadataValidator.IsValidId(appearance) ? appearance! : fallback;
        string root = Path.Combine(EmbeddedContent.Sprites, "mail-packs");
        string? metadata = Directory.EnumerateFiles(root, id + "-mail.json", SearchOption.AllDirectories).FirstOrDefault();
        if (metadata is null)
        {
            metadata = Directory.EnumerateFiles(root, fallback + "-mail.json", SearchOption.AllDirectories).Single();
            id = fallback;
        }
        return SpriteSheetLoader.Load(Path.GetDirectoryName(metadata)!, id + "-mail", mail: true);
    }

    private void OpenLetters()
    {
        if (_disposed || _visit is null || _visitor?.CanRead != true || _session != _online.Client.SessionId)
        {
            return;
        }
        if (_letters is null)
        {
            var window = new IncomingLettersWindow(_online, _visit);
            _letters = window;
            window.AllRead += AllRead;
            window.Closed += (_, _) =>
            {
                window.AllRead -= AllRead;
                if (ReferenceEquals(_letters, window))
                {
                    _letters = null;
                }
            };
            window.Show();
        }
        _letters?.Activate();
    }

    private void AllRead()
    {
        if (_visit?.Complete == true)
        {
            _visitor?.RequestExit();
        }
    }

    private void Finished()
    {
        CloseVisit();
        _online.VisitCompleted();
        Published();
    }

    private void CloseLetters()
    {
        _letters?.Close();
        _letters = null;
    }

    private void CloseVisit()
    {
        CloseLetters();
        if (_visitor is not null)
        {
            _visitor.LettersRequested -= OpenLetters;
            _visitor.Finished -= Finished;
            _visitor.Dispose();
            _visitor = null;
        }
        _visit = null;
        _session = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _online.MailboxChanged -= Published;
        _online.Client.SessionChanged -= SessionChanged;
        _stop.Cancel();
        CloseVisit();
        _stop.Dispose();
    }
}

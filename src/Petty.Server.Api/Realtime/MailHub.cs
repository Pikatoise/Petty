using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Petty.Server.Api.Realtime;

[Authorize]
public sealed class MailHub : Hub;

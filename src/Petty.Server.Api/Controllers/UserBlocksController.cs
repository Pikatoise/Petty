using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Petty.Server.Application.Features.Friends;

namespace Petty.Server.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting("friends")]
[Route("api/v1/users/{userId:guid}/block")]
public sealed class UserBlocksController(ISender sender) : ControllerBase
{
    [HttpGet("~/api/v1/users/blocked")]
    [DisableRateLimiting]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IReadOnlyList<BlockedUserDto>> GetBlocked(CancellationToken cancellationToken) =>
        await sender.Send(new GetBlockedUsers(Guid.Parse(User.FindFirst("sub")!.Value)), cancellationToken);

    [HttpPost]
    public async Task<IActionResult> Block(Guid userId, CancellationToken cancellationToken)
    {
        await sender.Send(new SetUserBlock(Guid.Parse(User.FindFirst("sub")!.Value), userId, Block: true), cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken cancellationToken)
    {
        await sender.Send(new SetUserBlock(Guid.Parse(User.FindFirst("sub")!.Value), userId, Block: false), cancellationToken);
        return NoContent();
    }
}

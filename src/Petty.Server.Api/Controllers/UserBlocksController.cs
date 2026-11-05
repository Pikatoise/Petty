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

using System.ComponentModel.DataAnnotations;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Petty.Server.Application.Features.Friends;

namespace Petty.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/friends")]
public sealed class FriendsController(ISender sender) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<IReadOnlyList<FriendDto>> Get(CancellationToken cancellationToken) =>
        await sender.Send(new GetFriends(UserId), cancellationToken);

    [HttpGet("requests")]
    public async Task<FriendRequestsDto> Requests(CancellationToken cancellationToken) =>
        await sender.Send(new GetFriendRequests(UserId), cancellationToken);

    [HttpPost("requests")]
    [EnableRateLimiting("friends")]
    public async Task<IActionResult> CreateRequest(FriendRequestBody request, CancellationToken cancellationToken)
    {
        Guid requestId = await sender.Send(new RequestFriend(UserId, request.Login), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new
        {
            requestId
        });
    }

    [HttpPost("requests/{requestId:guid}/accept")]
    [EnableRateLimiting("friends")]
    public async Task<IActionResult> Accept(Guid requestId, CancellationToken cancellationToken)
    {
        await sender.Send(new RespondToFriendRequest(UserId, requestId, Accept: true), cancellationToken);
        return NoContent();
    }

    [HttpPost("requests/{requestId:guid}/decline")]
    [EnableRateLimiting("friends")]
    public async Task<IActionResult> Decline(Guid requestId, CancellationToken cancellationToken)
    {
        await sender.Send(new RespondToFriendRequest(UserId, requestId, Accept: false), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{friendUserId:guid}")]
    [EnableRateLimiting("friends")]
    public async Task<IActionResult> Remove(Guid friendUserId, CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveFriend(UserId, friendUserId), cancellationToken);
        return NoContent();
    }

    [HttpDelete("requests/{requestId:guid}")]
    [EnableRateLimiting("friends")]
    public async Task<IActionResult> Cancel(Guid requestId, CancellationToken cancellationToken)
    {
        await sender.Send(new CancelFriendRequest(UserId, requestId), cancellationToken);
        return NoContent();
    }
}

public sealed record FriendRequestBody([Required] string Login);

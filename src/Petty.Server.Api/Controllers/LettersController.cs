using System.ComponentModel.DataAnnotations;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Petty.Server.Application.Features.Letters;

namespace Petty.Server.Api.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/letters")]
public sealed class LettersController(ISender sender) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpPost]
    [EnableRateLimiting("letters")]
    public async Task<IActionResult> Send(SendLetterBody request, CancellationToken cancellationToken)
    {
        SentLetter result = await sender.Send(new SendLetter(UserId, request.RecipientUserId, request.Body, request.ClientRequestId), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("outgoing")]
    public async Task<LetterHistoryPage> Outgoing(CancellationToken cancellationToken, string? cursor = null, int limit = 50) =>
        await sender.Send(new GetLetterHistory(UserId, Incoming: false, cursor, limit), cancellationToken);

    [HttpGet("incoming")]
    public async Task<LetterHistoryPage> Incoming(CancellationToken cancellationToken, string? cursor = null, int limit = 50) =>
        await sender.Send(new GetLetterHistory(UserId, Incoming: true, cursor, limit), cancellationToken);

    [HttpGet("quota/{recipientUserId:guid}")]
    public async Task<MailQuota> Quota(Guid recipientUserId, CancellationToken cancellationToken) =>
        await sender.Send(new GetMailQuota(UserId, recipientUserId), cancellationToken);

    [HttpGet("conversations/{otherUserId:guid}")]
    public async Task<LetterHistoryPage> Conversation(Guid otherUserId, CancellationToken cancellationToken, string? cursor = null, int limit = 50) =>
        await sender.Send(new GetLetterHistory(UserId, Incoming: false, cursor, limit, otherUserId), cancellationToken);


    [HttpPost("{letterId:guid}/read")]
    public async Task<ReadLettersResult> Read(Guid letterId, CancellationToken cancellationToken) =>
        await sender.Send(new ReadLetters(UserId, [letterId]), cancellationToken);

    [HttpPost("read")]
    public async Task<ReadLettersResult> ReadMany(ReadLettersBody request, CancellationToken cancellationToken) =>
        await sender.Send(new ReadLetters(UserId, request.LetterIds), cancellationToken);
}

public sealed record SendLetterBody(Guid RecipientUserId, [Required] string Body, Guid ClientRequestId);
public sealed record ReadLettersBody([Required, MinLength(1), MaxLength(10)] Guid[] LetterIds);

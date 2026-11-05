using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Petty.Server.Application.Features.Delivery;

namespace Petty.Server.Api.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/mailbox")]
public sealed class MailboxController(ISender sender) : ControllerBase
{
    [HttpGet("next")]
    public async Task<IActionResult> Next(CancellationToken cancellationToken)
    {
        MailboxVisit? visit = await sender.Send(new GetNextVisit(Guid.Parse(User.FindFirst("sub")!.Value)), cancellationToken);
        return visit is null ? NoContent() : Ok(visit);
    }
}

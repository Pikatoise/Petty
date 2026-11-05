using Mediator;
using Microsoft.AspNetCore.Mvc;
using Petty.Server.Application.Health;

namespace Petty.Server.Api.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(ISender sender) : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive" });

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        bool ready = await sender.Send(new GetReadiness(), cancellationToken);
        return StatusCode(ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable,
            new
            {
                status = ready ? "ready" : "not_ready"
            });
    }
}

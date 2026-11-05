using System.ComponentModel.DataAnnotations;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Petty.Server.Application.Features.Profiles;
using Petty.Server.Domain.Accounts;

namespace Petty.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/me")]
public sealed class ProfileController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        AccountProfile profile = await sender.Send(new GetProfile(Guid.Parse(User.FindFirst("sub")!.Value)), cancellationToken);
        return Ok(new
        {
            profile.UserId,
            profile.Login,
            profile.PetName,
            profile.PetAssetId,
            profile.ProfileUpdatedUtc,
            sessionActive = true
        });
    }

    [HttpPut("pet")]
    public async Task<IActionResult> UpdatePet(PetProfileRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new UpdatePet(Guid.Parse(User.FindFirst("sub")!.Value), request.PetName, request.AssetId), cancellationToken);
        return NoContent();
    }
}

public sealed record PetProfileRequest(
    [Required] string PetName,
    [Required, StringLength(64)] string AssetId);

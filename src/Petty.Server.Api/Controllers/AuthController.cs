using System.ComponentModel.DataAnnotations;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Petty.Server.Api.Auth;
using Petty.Server.Application.Features.Auth;

namespace Petty.Server.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        Guid userId = await sender.Send(new Register(request.Login, request.Password, request.ConfirmPassword), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new
        {
            userId
        });
    }

    [HttpPost("login")]
    public async Task<TokenResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new Login(request.Login, request.Password), cancellationToken);

    [HttpPost("refresh")]
    public async Task<TokenResponse> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new Refresh(request.RefreshToken), cancellationToken);

    [Authorize]
    [AllowRevokedSessionForLogout]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sender.Send(new Logout(Guid.Parse(User.FindFirst("sub")!.Value), Guid.Parse(User.FindFirst("sid")!.Value)), cancellationToken);
        return NoContent();
    }
}

public sealed record RegisterRequest(
    [Required] string Login,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required, StringLength(128)] string ConfirmPassword);

public sealed record LoginRequest(
    [Required] string Login,
    [Required, StringLength(128)] string Password);

public sealed record RefreshRequest([Required, StringLength(97, MinimumLength = 97)] string RefreshToken);

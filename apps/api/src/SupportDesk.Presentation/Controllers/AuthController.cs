using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Features.Auth.Commands.LoginAgent;
using SupportDesk.Application.Features.Auth.Queries.GetCurrentAgent;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        [FromServices] LoginAgentCommandHandler handler,
        CancellationToken ct)
    {
        var response = await handler.HandleAsync(request, ct);
        if (response is not null)
        {
            return Ok(response);
        }

        return Unauthorized(new ProblemDetails
        {
            Title = "Unauthorized",
            Detail = InvalidCredentialsMessage,
            Status = StatusCodes.Status401Unauthorized
        });
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthenticatedAgentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticatedAgentDto>> Me(
        [FromServices] GetCurrentAgentQueryHandler handler,
        CancellationToken ct)
    {
        var agentIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!int.TryParse(agentIdClaim, out var agentId) || agentId <= 0)
        {
            return Unauthorized();
        }

        var agent = await handler.HandleAsync(agentId, ct);
        return agent is null ? Unauthorized() : Ok(agent);
    }
}

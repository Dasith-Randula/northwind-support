using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Commands.LoginAgent;

public sealed class LoginAgentCommandHandler(
    IAgentRepository agents,
    IAgentPasswordHasher passwordHasher,
    IAccessTokenIssuer tokenIssuer)
{
    public async Task<LoginResponse?> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        var agent = await agents.GetByEmailAsync(email, ct);

        if (agent is null || !agent.IsActive ||
            !passwordHasher.VerifyPassword(agent, agent.PasswordHash, request.Password))
        {
            return null;
        }

        var token = tokenIssuer.Issue(agent);
        return new LoginResponse(
            token.AccessToken,
            token.ExpiresAtUtc,
            new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email));
    }
}

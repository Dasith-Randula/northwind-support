using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Auth.Queries.GetCurrentAgent;

public sealed class GetCurrentAgentQueryHandler(IAgentRepository agents)
{
    public async Task<AuthenticatedAgentDto?> HandleAsync(int agentId, CancellationToken ct)
    {
        var agent = await agents.GetByIdAsync(agentId, ct);
        return agent is null ? null : new AuthenticatedAgentDto(agent.Id, agent.FullName, agent.Email);
    }
}

using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Queries.GetEscalations;

public sealed class GetEscalationsQueryHandler(ITicketRepository tickets, IAgentRepository agents)
{
    public async Task<IReadOnlyList<TicketEscalationDto>> HandleAsync(int ticketId, CancellationToken ct)
    {
        if (!await tickets.ExistsAsync(ticketId, ct))
        {
            throw new NotFoundException("Ticket", ticketId);
        }

        var history = await tickets.GetEscalationsAsync(ticketId, ct);
        var ids = history.SelectMany(x => new[] { x.FromAgentId, x.ToAgentId })
            .OfType<int>().Distinct().ToArray();
        var lookup = (await agents.GetAllAsync(ct)).Where(x => ids.Contains(x.Id))
            .ToDictionary(x => x.Id);

        return history.Select(item => new TicketEscalationDto
        {
            Id = item.Id,
            TicketId = item.TicketId,
            FromPriority = item.FromPriority,
            ToPriority = item.ToPriority,
            FromAgent = item.FromAgentId is int fromId && lookup.TryGetValue(fromId, out var from)
                ? new AgentSummaryDto(from.Id, from.FullName) : null,
            ToAgent = item.ToAgentId is int toId && lookup.TryGetValue(toId, out var to)
                ? new AgentSummaryDto(to.Id, to.FullName) : null,
            FromDueAtUtc = item.FromDueAtUtc,
            ToDueAtUtc = item.ToDueAtUtc,
            Reason = item.Reason,
            EscalatedBy = item.EscalatedBy,
            EscalatedAtUtc = item.EscalatedAtUtc
        }).ToList();
    }
}

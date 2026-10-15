using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.Domain.Exceptions;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;

public sealed class EscalateTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    SlaCalculator slaCalculator,
    SlaPolicySettings slaSettings,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<EscalateTicketCommandHandler> logger)
{
    public async Task<EscalateTicketResponse> HandleAsync(
        int id,
        EscalateTicketRequest request,
        CancellationToken ct)
    {
        var ticket = await tickets.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Ticket", id);

        if (ticket.Priority == TicketPriority.Critical)
        {
            throw new ConflictException($"Ticket {ticket.Reference} is already Critical and cannot be escalated.");
        }

        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            throw new ConflictException(
                $"Ticket {ticket.Reference} is {ticket.Status} and cannot be escalated.");
        }

        var customer = await customers.GetByIdAsync(ticket.CustomerId, ct)
            ?? throw new NotFoundException("Customer", ticket.CustomerId);
        var category = await categories.GetByIdAsync(ticket.CategoryId, ct)
            ?? throw new NotFoundException("Category", ticket.CategoryId);
        var availableAgents = await agents.GetAllAsync(ct);
        var counts = await tickets.GetOpenTicketCountsAsync(
            availableAgents.Select(agent => agent.Id).ToArray(), ct);

        var escalatedAtUtc = clock.UtcNow;
        var toPriority = ticket.Priority + 1;
        var sla = slaCalculator.Calculate(toPriority, customer.Tier, escalatedAtUtc);
        var currentAgent = ticket.AssignedAgentId is int currentAgentId
            ? availableAgents.FirstOrDefault(agent => agent.Id == currentAgentId)
            : null;
        var currentAgentCounts = counts.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (ticket.AssignedAgentId is int assignedAgentId && currentAgentCounts.ContainsKey(assignedAgentId))
        {
            currentAgentCounts[assignedAgentId]--;
        }

        var currentAgentEligible = currentAgent is not null
            && AgentSelectionPolicy.IsEligible(category, currentAgent, currentAgentCounts);
        var assignment = currentAgentEligible
            ? new AgentSelectionResult(currentAgent!.Id, $"Kept assignment to {currentAgent.FullName}; the agent remains eligible.")
            : AgentSelectionPolicy.Select(category, availableAgents, counts);

        var escalation = new TicketEscalation(
            ticket.Id,
            ticket.Priority,
            toPriority,
            ticket.AssignedAgentId,
            assignment.AgentId,
            ticket.DueAtUtc,
            sla.DueAtUtc,
            request.Reason!,
            request.EscalatedBy!,
            escalatedAtUtc);

        ticket.Escalate(toPriority, sla.DueAtUtc, assignment.AgentId, escalatedAtUtc);
        await tickets.AddEscalationAsync(escalation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} escalated from {FromPriority} to {ToPriority}.",
            ticket.Reference, escalation.FromPriority, escalation.ToPriority);

        var result = await getTicket.HandleAsync(id, ct);
        result.SlaStatus = SlaEvaluator.Evaluate(
            ticket.DueAtUtc,
            ticket.ResolvedAtUtc,
            escalatedAtUtc,
            sla.EffectiveWindow,
            slaSettings.AtRiskThresholdPercent);
        var agentLookup = availableAgents.ToDictionary(agent => agent.Id);

        return new EscalateTicketResponse
        {
            Ticket = result,
            Escalation = new TicketEscalationDto
            {
                Id = escalation.Id,
                TicketId = escalation.TicketId,
                FromPriority = escalation.FromPriority,
                ToPriority = escalation.ToPriority,
                FromAgent = escalation.FromAgentId is int fromId
                    && agentLookup.TryGetValue(fromId, out var fromAgent)
                    ? new AgentSummaryDto(fromAgent.Id, fromAgent.FullName)
                    : null,
                ToAgent = escalation.ToAgentId is int toId
                    && agentLookup.TryGetValue(toId, out var toAgent)
                    ? new AgentSummaryDto(toAgent.Id, toAgent.FullName)
                    : null,
                FromDueAtUtc = escalation.FromDueAtUtc,
                ToDueAtUtc = escalation.ToDueAtUtc,
                Reason = escalation.Reason,
                EscalatedBy = escalation.EscalatedBy,
                EscalatedAtUtc = escalation.EscalatedAtUtc
            }
        };
    }
}

using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket for a customer.
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    SlaCalculator slaCalculator,
    SlaPolicySettings slaSettings,
    IUnitOfWork unitOfWork,
    ITicketCreationCoordinator ticketCreation,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        return await ticketCreation.ExecuteAsync(async operationCt =>
        {
            var customer = await customers.GetByIdAsync(request.CustomerId, operationCt);
            if (customer is null)
            {
                throw new NotFoundException("Customer", request.CustomerId);
            }

            var category = await categories.GetByIdAsync(request.CategoryId, operationCt);
            if (category is null)
            {
                throw new NotFoundException("Category", request.CategoryId);
            }

            var createdAtUtc = clock.UtcNow;
            var priority = PriorityPolicy.Determine(request.RequestedPriority, category);
            var sla = slaCalculator.Calculate(priority.Priority, customer.Tier, createdAtUtc);
            var availableAgents = await agents.GetAllAsync(operationCt);
            var openTicketCounts = await tickets.GetOpenTicketCountsAsync(
                availableAgents.Select(agent => agent.Id).ToArray(),
                operationCt);
            var assignment = AgentSelectionPolicy.Select(category, availableAgents, openTicketCounts);

            var ticket = Ticket.Raise(
                await tickets.NextReferenceAsync(operationCt),
                request.Title,
                request.Description,
                request.CustomerId,
                request.CategoryId,
                priority.Priority,
                createdAtUtc,
                sla.DueAtUtc,
                assignment.AgentId);

            await tickets.AddAsync(ticket, operationCt);
            await unitOfWork.SaveChangesAsync(operationCt);

            logger.LogInformation(
                "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}.",
                ticket.Reference, ticket.CustomerId, ticket.CategoryId);

            var result = await getTicket.HandleAsync(ticket.Id, operationCt);
            result.Triage = new TicketTriageDto(
                priority.Priority,
                priority.Reason,
                sla.EffectiveWindow.TotalHours,
                sla.Reason,
                assignment.Reason);
            result.SlaStatus = SlaEvaluator.Evaluate(
                ticket.DueAtUtc,
                ticket.ResolvedAtUtc,
                createdAtUtc,
                sla.EffectiveWindow,
                slaSettings.AtRiskThresholdPercent);

            return result;
        }, ct);
    }
}

using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.BusinessRules;

public static class AgentSelectionPolicy
{
    public static IReadOnlyDictionary<int, int> CountOpenTickets(
        IEnumerable<Agent> agents,
        IEnumerable<Ticket> tickets)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(tickets);

        var counts = agents.ToDictionary(agent => agent.Id, _ => 0);

        foreach (var ticket in tickets)
        {
            if (ticket.AssignedAgentId is int agentId
                && ticket.Status is not (TicketStatus.Resolved or TicketStatus.Closed)
                && counts.ContainsKey(agentId))
            {
                counts[agentId]++;
            }
        }

        return counts;
    }

    public static AgentSelectionResult Select(
        Category category,
        IEnumerable<Agent> agents,
        IReadOnlyDictionary<int, int> openTicketCounts)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(openTicketCounts);

        var eligible = agents
            .Where(agent => IsEligible(category, agent, openTicketCounts))
            .OrderBy(agent => openTicketCounts.GetValueOrDefault(agent.Id))
            .ThenBy(agent => agent.Id)
            .ToList();

        if (eligible.Count == 0)
        {
            var requirement = category.RequiresSpecialist
                ? $" with '{category.Name}' specialization"
                : string.Empty;

            return new AgentSelectionResult(
                null,
                $"No active agent below capacity{requirement} was eligible; the ticket remains unassigned.");
        }

        var selected = eligible[0];
        return new AgentSelectionResult(
            selected.Id,
            $"Assigned to {selected.FullName}, the eligible agent with the fewest open tickets.");
    }

    public static bool IsEligible(
        Category category,
        Agent agent,
        IReadOnlyDictionary<int, int> openTicketCounts)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(openTicketCounts);

        var openTickets = openTicketCounts.GetValueOrDefault(agent.Id);
        var hasSpecialization = agent.Specializations.Any(s => s.CategoryId == category.Id);

        return agent.IsActive
            && openTickets < agent.MaxOpenTickets
            && (!category.RequiresSpecialist || hasSpecialization);
    }
}

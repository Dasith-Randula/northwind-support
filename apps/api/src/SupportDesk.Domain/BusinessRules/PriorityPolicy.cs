using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.BusinessRules;

public static class PriorityPolicy
{
    public static PriorityDecision Determine(TicketPriority? requestedPriority, Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.ForcesCriticalPriority)
        {
            return new PriorityDecision(
                TicketPriority.Critical,
                $"Category '{category.Name}' forces Critical priority.");
        }

        var priority = requestedPriority ?? TicketPriority.Medium;
        var reason = requestedPriority is null
            ? "No priority was requested, so the default Medium priority was applied."
            : $"The requested {priority} priority was applied.";

        return new PriorityDecision(priority, reason);
    }
}

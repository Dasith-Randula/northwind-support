using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.BusinessRules;

public sealed record PriorityDecision(TicketPriority Priority, string Reason);

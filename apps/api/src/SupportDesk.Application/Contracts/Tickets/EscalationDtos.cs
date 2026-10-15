using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Contracts.Tickets;

public sealed record EscalateTicketRequest(string? Reason, string? EscalatedBy);

public sealed class EscalateTicketResponse
{
    public TicketDetailDto Ticket { get; init; } = null!;

    public TicketEscalationDto Escalation { get; init; } = null!;
}

public sealed class TicketEscalationDto
{
    public int Id { get; init; }
    public int TicketId { get; init; }
    public TicketPriority FromPriority { get; init; }
    public TicketPriority ToPriority { get; init; }
    public AgentSummaryDto? FromAgent { get; init; }
    public AgentSummaryDto? ToAgent { get; init; }
    public DateTime? FromDueAtUtc { get; init; }
    public DateTime? ToDueAtUtc { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string EscalatedBy { get; init; } = string.Empty;
    public DateTime EscalatedAtUtc { get; init; }
}

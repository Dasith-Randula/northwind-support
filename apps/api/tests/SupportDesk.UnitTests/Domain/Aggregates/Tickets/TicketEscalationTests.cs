using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class TicketEscalationTests
{
    private static readonly DateTime Now = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EscalateRaisesExactlyOneLevelAndUpdatesDeadlineAndAssignment()
    {
        var ticket = Ticket.Raise("TCK-1", "Title", "Description", 1, 1, TicketPriority.Low, Now);

        ticket.Escalate(TicketPriority.Medium, Now.AddHours(24), 2, Now.AddMinutes(5));

        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        Assert.Equal(Now.AddHours(24), ticket.DueAtUtc);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Equal(Now.AddMinutes(5), ticket.UpdatedAtUtc);
    }

    [Fact]
    public void EscalateRejectsSkippedPriority()
    {
        var ticket = Ticket.Raise("TCK-1", "Title", "Description", 1, 1, TicketPriority.Low, Now);

        Assert.Throws<BusinessRuleViolationException>(() =>
            ticket.Escalate(TicketPriority.High, Now.AddHours(8), null, Now));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void EscalateRejectsCompletedTickets(TicketStatus status)
    {
        var ticket = Ticket.Raise("TCK-1", "Title", "Description", 1, 1, TicketPriority.Low, Now);
        ticket.ChangeStatus(status, Now);

        Assert.Throws<BusinessRuleViolationException>(() =>
            ticket.Escalate(TicketPriority.Medium, Now.AddHours(24), null, Now));
    }
}

using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public sealed class EscalateTicketCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_KeepsCurrentEligibleAgentAndReturnsRecordedEscalation()
    {
        var context = CreateContext(TicketPriority.Low, assignedAgentId: 2);
        var agent = Agent(2, "Ben Osei", maxOpenTickets: 3);
        context.Agents.Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([agent]);
        context.Tickets.Setup(t => t.GetOpenTicketCountsAsync(
                It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int> { [2] = 1 });

        var response = await context.EscalateTicket.HandleAsync(
            41,
            new EscalateTicketRequest("Customer impact increased", "lead.user"),
            CancellationToken.None);

        Assert.NotNull(context.AddedEscalation);
        Assert.Equal(response.Escalation.FromPriority, context.AddedEscalation!.FromPriority);
        Assert.Equal(response.Escalation.ToPriority, context.AddedEscalation.ToPriority);
        Assert.Equal(response.Escalation.FromDueAtUtc, context.AddedEscalation.FromDueAtUtc);
        Assert.Equal(response.Escalation.ToDueAtUtc, context.AddedEscalation.ToDueAtUtc);
        Assert.Equal(response.Escalation.Reason, context.AddedEscalation.Reason);
        Assert.Equal(response.Escalation.EscalatedBy, context.AddedEscalation.EscalatedBy);
        Assert.Equal(response.Escalation.EscalatedAtUtc, context.AddedEscalation.EscalatedAtUtc);
        Assert.Equal(TicketPriority.Low, response.Escalation.FromPriority);
        Assert.Equal(TicketPriority.Medium, response.Escalation.ToPriority);
        Assert.Equal(2, response.Escalation.FromAgent!.Id);
        Assert.Equal(2, response.Escalation.ToAgent!.Id);
        Assert.Equal("Customer impact increased", response.Escalation.Reason);
        Assert.Equal("lead.user", response.Escalation.EscalatedBy);
        Assert.Equal(FixedClock.DefaultNow, response.Escalation.EscalatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow.AddHours(12), response.Escalation.ToDueAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_ReassignsIneligibleCurrentAgentAndRecalculatesPremiumSla()
    {
        var context = CreateContext(TicketPriority.Medium, assignedAgentId: 2);
        var current = Agent(2, "Inactive Agent", 3);
        current.Deactivate();
        var replacement = Agent(4, "Sara Lindqvist", 3);
        context.Agents.Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([current, replacement]);
        context.Tickets.Setup(t => t.GetOpenTicketCountsAsync(
                It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int> { [2] = 1, [4] = 0 });

        var response = await context.EscalateTicket.HandleAsync(
            41,
            new EscalateTicketRequest("More urgent customer impact", "lead.user"),
            CancellationToken.None);

        Assert.Equal(4, response.Ticket.AssignedAgent!.Id);
        Assert.Equal(2, response.Escalation.FromAgent!.Id);
        Assert.Equal(4, response.Escalation.ToAgent!.Id);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), response.Escalation.ToDueAtUtc);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), response.Ticket.DueAtUtc);
        Assert.Equal(TicketPriority.High, response.Ticket.Priority);
        context.VerifySaved(Times.Once());
    }

    [Theory]
    [InlineData(TicketPriority.Critical, TicketStatus.New)]
    [InlineData(TicketPriority.Low, TicketStatus.Resolved)]
    [InlineData(TicketPriority.Low, TicketStatus.Closed)]
    public async Task HandleAsync_RejectsInvalidStateWithoutSaving(
        TicketPriority priority,
        TicketStatus status)
    {
        var context = CreateContext(priority, status: status);

        await Assert.ThrowsAsync<ConflictException>(() =>
            context.EscalateTicket.HandleAsync(
                41,
                new EscalateTicketRequest("Valid escalation reason", "lead.user"),
                CancellationToken.None));

        Assert.Null(context.AddedEscalation);
        context.VerifySaved(Times.Never());
    }

    private static TicketCommandTestContext CreateContext(
        TicketPriority priority,
        TicketStatus status = TicketStatus.New,
        int? assignedAgentId = null)
    {
        var context = new TicketCommandTestContext();
        var ticket = Ticket.Raise(
            "TCK-0041",
            "Cannot log in",
            "The customer cannot access the portal.",
            1,
            3,
            priority,
            FixedClock.DefaultNow,
            FixedClock.DefaultNow.AddHours(72),
            assignedAgentId);
        SetId(ticket, 41);
        if (status != TicketStatus.New)
        {
            ticket.ChangeStatus(status, FixedClock.DefaultNow);
        }

        context.ExistingTicket(ticket);
        return context;
    }

    private static Agent Agent(int id, string name, int maxOpenTickets) =>
        SetId(new Agent(name, $"{name.Replace(" ", ".").ToLowerInvariant()}@example.com",
            maxOpenTickets, FixedClock.DefaultNow), id);

    private static T SetId<T>(T entity, int id)
    {
        typeof(SupportDesk.Domain.Common.Entity)
            .GetProperty(nameof(SupportDesk.Domain.Common.Entity.Id))!
            .SetValue(entity, id);
        return entity;
    }
}

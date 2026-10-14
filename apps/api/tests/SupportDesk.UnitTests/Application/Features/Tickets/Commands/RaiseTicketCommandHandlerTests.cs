using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_AppliesForcedCriticalPriorityPremiumSlaAndTriage()
    {
        var context = new TicketCommandTestContext();
        var category = SetId(new Category("Security", true, true), 3);
        context.ResponseCategory = category;
        context.Categories
            .Setup(c => c.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var result = await context.RaiseTicket.HandleAsync(
            Request(TicketPriority.Low),
            CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Critical, context.AddedTicket.Priority);
        Assert.Equal(FixedClock.DefaultNow.AddHours(2), context.AddedTicket.DueAtUtc);
        Assert.Equal(context.AddedTicket.Reference, result.Reference);
        Assert.Equal(context.AddedTicket.Title, result.Title);
        Assert.Equal(context.AddedTicket.Description, result.Description);
        Assert.Equal(context.AddedTicket.Status, result.Status);
        Assert.Equal(context.AddedTicket.Priority, result.Priority);
        Assert.Equal(context.AddedTicket.CreatedAtUtc, result.CreatedAtUtc);
        Assert.Equal(context.AddedTicket.UpdatedAtUtc, result.UpdatedAtUtc);
        Assert.Equal(context.AddedTicket.DueAtUtc, result.DueAtUtc);
        Assert.Equal(1, result.Customer.Id);
        Assert.Equal(CustomerTier.Premium, result.Customer.Tier);
        Assert.Equal(category.Id, result.Category.Id);
        Assert.Equal(category.Name, result.Category.Name);
        Assert.Null(result.AssignedAgent);
        Assert.Equal(TicketPriority.Critical, result.Triage!.AppliedPriority);
        Assert.Contains("Critical", result.Triage.PriorityReason);
        Assert.Equal(2, result.Triage.SlaWindowHours);
        Assert.Contains("Premium", result.Triage.SlaReason);
        Assert.Contains("unassigned", result.Triage.AssignmentReason);
        Assert.Equal(SlaStatus.WithinSla, result.SlaStatus);
    }

    [Fact]
    public async Task HandleAsync_SelectsEligibleAgentUsingDatabaseWorkloadCounts()
    {
        var context = new TicketCommandTestContext();
        var category = SetId(new Category("Security", true, false), 3);
        context.ResponseCategory = category;
        var specialist = SetId(new Agent("Priya Nair", "priya@example.com", 8, FixedClock.DefaultNow), 3);
        specialist.AddSpecialization(category.Id);
        var other = SetId(new Agent("Other", "other@example.com", 8, FixedClock.DefaultNow), 4);

        context.Categories
            .Setup(c => c.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        context.Agents
            .Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([specialist, other]);
        context.Tickets
            .Setup(t => t.GetOpenTicketCountsAsync(
                It.IsAny<IReadOnlyCollection<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int> { [specialist.Id] = 2, [other.Id] = 0 });

        var result = await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(specialist.Id, context.AddedTicket!.AssignedAgentId);
        Assert.Equal(specialist.Id, result.AssignedAgent!.Id);
        Assert.Equal(specialist.FullName, result.AssignedAgent.FullName);
        Assert.Equal(context.AddedTicket.Priority, result.Priority);
        Assert.Equal(context.AddedTicket.DueAtUtc, result.DueAtUtc);
        Assert.NotNull(result.Triage);
        Assert.Contains("Assigned", result.Triage.AssignmentReason);
    }

    [Fact]
    public async Task HandleAsync_WhenNoAgentIsEligible_PersistsUnassignedTicketWithReason()
    {
        var context = new TicketCommandTestContext();
        var category = SetId(new Category("Security", true, false), 3);
        context.ResponseCategory = category;
        var fullSpecialist = SetId(new Agent("Full", "full@example.com", 1, FixedClock.DefaultNow), 3);
        fullSpecialist.AddSpecialization(category.Id);

        context.Categories
            .Setup(c => c.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);
        context.Agents
            .Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([fullSpecialist]);
        context.Tickets
            .Setup(t => t.GetOpenTicketCountsAsync(
                It.IsAny<IReadOnlyCollection<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int> { [fullSpecialist.Id] = 1 });

        var result = await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.Null(context.AddedTicket!.AssignedAgentId);
        Assert.Contains("unassigned", result.Triage!.AssignmentReason);
        Assert.Null(result.AssignedAgent);
        Assert.Equal(context.AddedTicket.Priority, result.Priority);
        Assert.Equal(context.AddedTicket.DueAtUtc, result.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.Customers
            .Setup(c => c.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        context.Customers
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupportDesk.Domain.Aggregates.Customers.Customer?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryDoesNotExist_ThrowsWithoutPersisting()
    {
        var context = new TicketCommandTestContext();
        context.Categories
            .Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Category?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Category", exception.Message);
        Assert.Null(context.AddedTicket);
        context.VerifySaved(Times.Never());
    }

    private static T SetId<T>(T entity, int id)
    {
        typeof(SupportDesk.Domain.Common.Entity)
            .GetProperty(nameof(SupportDesk.Domain.Common.Entity.Id))!
            .SetValue(entity, id);
        return entity;
    }
}

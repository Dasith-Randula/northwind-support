using Microsoft.Extensions.Logging.Abstractions;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Features.Tickets.Commands.AssignTicket;
using SupportDesk.Application.Features.Tickets.Commands.ChangeTicketStatus;
using SupportDesk.Application.Features.Tickets.Commands.EscalateTicket;
using SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.Domain.Repositories;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

/// <summary>
/// Shared arrangement for the ticket command tests: mocked repositories and queries, a fixed
/// clock, and command handlers wired to all of them.
/// </summary>
internal sealed class TicketCommandTestContext
{
    public TicketCommandTestContext()
    {
        Customers.Setup(c => c.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        Customers.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SetId(new Customer(
                "Contoso Ltd",
                "support@contoso.example",
                null,
                CustomerTier.Premium,
                Clock.UtcNow), 1));

        Categories.Setup(c => c.ExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        Categories.Setup(c => c.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SetId(new Category("General", false, false), 3));

        Agents.Setup(a => a.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Agent>());
        Tickets.Setup(t => t.GetOpenTicketCountsAsync(
                It.IsAny<IReadOnlyCollection<int>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<int, int>());

        Tickets.Setup(t => t.NextReferenceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("TCK-0041");

        Tickets.Setup(t => t.AddAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .Callback<Ticket, CancellationToken>((ticket, _) => AddedTicket = ticket)
            .Returns(Task.CompletedTask);
        Tickets.Setup(t => t.AddEscalationAsync(
                It.IsAny<TicketEscalation>(),
                It.IsAny<CancellationToken>()))
            .Callback<TicketEscalation, CancellationToken>((escalation, _) => AddedEscalation = escalation)
            .Returns(Task.CompletedTask);

        // Every command returns the ticket as re-read once its change has been saved.
        TicketQueries.Setup(q => q.GetDetailAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildCreatedTicketDetail);
        TicketCreation.Setup(c => c.ExecuteAsync(
                It.IsAny<Func<CancellationToken, Task<TicketDetailDto>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<TicketDetailDto>> operation, CancellationToken ct) =>
                operation(ct));

        var getTicket = new GetTicketQueryHandler(TicketQueries.Object);

        RaiseTicket = new RaiseTicketCommandHandler(
            Tickets.Object, Customers.Object, Categories.Object, Agents.Object,
            new SlaCalculator(new SlaPolicySettings()), new SlaPolicySettings(),
            UnitOfWork.Object, TicketCreation.Object, Clock, getTicket,
            NullLogger<RaiseTicketCommandHandler>.Instance);

        ChangeTicketStatus = new ChangeTicketStatusCommandHandler(Tickets.Object, UnitOfWork.Object, Clock, getTicket);

        AssignTicket = new AssignTicketCommandHandler(
            Tickets.Object, Agents.Object, UnitOfWork.Object, Clock, getTicket,
            NullLogger<AssignTicketCommandHandler>.Instance);

        EscalateTicket = new EscalateTicketCommandHandler(
            Tickets.Object, Customers.Object, Categories.Object, Agents.Object,
            new SlaCalculator(new SlaPolicySettings()), new SlaPolicySettings(),
            UnitOfWork.Object, Clock, getTicket,
            NullLogger<EscalateTicketCommandHandler>.Instance);
    }

    public Mock<ITicketRepository> Tickets { get; } = new();

    public Mock<ICustomerRepository> Customers { get; } = new();

    public Mock<ICategoryRepository> Categories { get; } = new();

    public Mock<IAgentRepository> Agents { get; } = new();

    public Mock<IUnitOfWork> UnitOfWork { get; } = new();

    public Mock<ITicketCreationCoordinator> TicketCreation { get; } = new();

    public Mock<ITicketQueries> TicketQueries { get; } = new();

    public FixedClock Clock { get; } = new();

    public RaiseTicketCommandHandler RaiseTicket { get; }

    public ChangeTicketStatusCommandHandler ChangeTicketStatus { get; }

    public AssignTicketCommandHandler AssignTicket { get; }

    public EscalateTicketCommandHandler EscalateTicket { get; }

    /// <summary>The ticket handed to the repository by the last create call.</summary>
    public Ticket? AddedTicket { get; private set; }

    public Ticket? ExistingResponseTicket { get; private set; }

    public TicketEscalation? AddedEscalation { get; private set; }

    public Customer ResponseCustomer { get; set; } = SetId(new Customer(
        "Contoso Ltd",
        "support@contoso.example",
        null,
        CustomerTier.Premium,
        FixedClock.DefaultNow), 1);

    public Category ResponseCategory { get; set; } = SetId(new Category("General", false, false), 3);

    public Dictionary<int, string> ResponseAgentNames { get; } = new()
    {
        [1] = "Alex Turner",
        [2] = "Ben Osei",
        [3] = "Priya Nair",
        [4] = "Sara Lindqvist",
        [5] = "Marcus Doyle",
        [6] = "Yuki Tanaka"
    };

    /// <summary>Makes <see cref="ITicketRepository.GetByIdAsync"/> return this ticket.</summary>
    public Ticket ExistingTicket(Ticket ticket)
    {
        ExistingResponseTicket = ticket;
        Tickets.Setup(t => t.GetByIdAsync(ticket.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        return ticket;
    }

    public void ExistingAgent(int id, string fullName = "Alex Turner", bool isActive = true)
    {
        var agent = new Agent(fullName, $"agent{id}@example.com", maxOpenTickets: 10, Clock.UtcNow);

        if (!isActive)
        {
            agent.Deactivate();
        }

        Agents.Setup(a => a.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(agent);
    }

    public void VerifySaved(Times times) =>
        UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), times);

    private TicketDetailDto BuildCreatedTicketDetail()
    {
        var ticket = AddedTicket ?? ExistingResponseTicket
            ?? throw new InvalidOperationException("No ticket was available for the detail response.");
        var assignedAgent = ticket.AssignedAgentId is int agentId
            ? new AgentSummaryDto(agentId, ResponseAgentNames.GetValueOrDefault(agentId, $"Agent {agentId}"))
            : null;

        return new TicketDetailDto
        {
            Id = 41,
            Reference = ticket.Reference,
            Title = ticket.Title,
            Description = ticket.Description,
            Status = ticket.Status,
            Priority = ticket.Priority,
            Customer = new CustomerContactDto(
                ResponseCustomer.Id,
                ResponseCustomer.Name,
                ResponseCustomer.Email,
                ResponseCustomer.Phone,
                ResponseCustomer.Tier),
            Category = new CategoryDto(
                ResponseCategory.Id,
                ResponseCategory.Name,
                ResponseCategory.RequiresSpecialist),
            AssignedAgent = assignedAgent,
            CreatedAtUtc = ticket.CreatedAtUtc,
            UpdatedAtUtc = ticket.UpdatedAtUtc,
            DueAtUtc = ticket.DueAtUtc,
            ResolvedAtUtc = ticket.ResolvedAtUtc,
            SlaStatus = SlaEvaluator.Evaluate(
                ticket.DueAtUtc,
                ticket.ResolvedAtUtc,
                Clock.UtcNow)
        };
    }

    private static T SetId<T>(T entity, int id)
    {
        typeof(SupportDesk.Domain.Common.Entity)
            .GetProperty(nameof(SupportDesk.Domain.Common.Entity.Id))!
            .SetValue(entity, id);
        return entity;
    }
}

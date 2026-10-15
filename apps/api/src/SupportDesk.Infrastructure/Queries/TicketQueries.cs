using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Common;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.BusinessRules;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>
/// Ticket reads. Projected in the database and never tracked. Aggregates hold only each
/// other's ids, so the customer, category and agent labels are joined in here, on the read side.
/// </summary>
public sealed class TicketQueries(
    SupportDbContext db,
    IClock clock,
    SlaPolicySettings slaSettings) : ITicketQueries
{
    /// <remarks>
    /// Applies filters, then sorts and pages the ticket list in the database.
    /// </remarks>
    public async Task<PagedResult<TicketListItemDto>> GetPagedAsync(TicketQuery query, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tickets = ApplyFilters(TicketsWithLabels(), query, now);

        var totalCount = await tickets.CountAsync(ct);

        var items = await ApplySort(tickets, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = EvaluateStatus(
                    x.Ticket.Priority,
                    x.Ticket.DueAtUtc,
                    x.Ticket.ResolvedAtUtc,
                    x.Customer.Tier,
                    now,
                    slaSettings)
            })
            .ToListAsync(ct);

        return new PagedResult<TicketListItemDto>(items, query.Page, query.PageSize, totalCount);
    }

    public Task<TicketDetailDto?> GetDetailAsync(int id, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return TicketsWithLabels()
            .Where(x => x.Ticket.Id == id)
            .Select(x => new TicketDetailDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Description = x.Ticket.Description,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerContactDto(
                    x.Customer.Id, x.Customer.Name, x.Customer.Email, x.Customer.Phone, x.Customer.Tier),
                Category = new CategoryDto(x.Category.Id, x.Category.Name, x.Category.RequiresSpecialist),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = EvaluateStatus(
                    x.Ticket.Priority,
                    x.Ticket.DueAtUtc,
                    x.Ticket.ResolvedAtUtc,
                    x.Customer.Tier,
                    now,
                    slaSettings)
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<TicketListItemDto>> GetForCustomerAsync(int customerId, CancellationToken ct)
    {
        var now = clock.UtcNow;

        return await TicketsWithLabels()
            .Where(x => x.Ticket.CustomerId == customerId)
            .OrderByDescending(x => x.Ticket.CreatedAtUtc)
            .ThenByDescending(x => x.Ticket.Id)
            .Select(x => new TicketListItemDto
            {
                Id = x.Ticket.Id,
                Reference = x.Ticket.Reference,
                Title = x.Ticket.Title,
                Status = x.Ticket.Status,
                Priority = x.Ticket.Priority,
                Customer = new CustomerSummaryDto(x.Customer.Id, x.Customer.Name, x.Customer.Tier),
                Category = new CategorySummaryDto(x.Category.Id, x.Category.Name),
                AssignedAgent = x.Agent == null ? null : new AgentSummaryDto(x.Agent.Id, x.Agent.FullName),
                CreatedAtUtc = x.Ticket.CreatedAtUtc,
                UpdatedAtUtc = x.Ticket.UpdatedAtUtc,
                DueAtUtc = x.Ticket.DueAtUtc,
                ResolvedAtUtc = x.Ticket.ResolvedAtUtc,
                SlaStatus = EvaluateStatus(
                    x.Ticket.Priority,
                    x.Ticket.DueAtUtc,
                    x.Ticket.ResolvedAtUtc,
                    x.Customer.Tier,
                    now,
                    slaSettings)
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Every ticket with the rows that label it. Filter and sort this, then project.
    /// </summary>
    private IQueryable<TicketWithLabels> TicketsWithLabels() =>
        from ticket in db.Set<Ticket>().AsNoTracking()
        join customer in db.Set<Customer>() on ticket.CustomerId equals customer.Id
        join category in db.Set<Category>() on ticket.CategoryId equals category.Id
        from agent in db.Set<Agent>().Where(a => a.Id == ticket.AssignedAgentId).DefaultIfEmpty()
        select new TicketWithLabels { Ticket = ticket, Customer = customer, Category = category, Agent = agent };

    private IQueryable<TicketWithLabels> ApplyFilters(
        IQueryable<TicketWithLabels> source,
        TicketQuery query,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            var normalizedSearch = search.ToLower();

            source = source.Where(x =>
                x.Ticket.Title.ToLower().Contains(normalizedSearch) ||
                x.Ticket.Reference.ToLower().Contains(normalizedSearch) ||
                x.Customer.Name.ToLower().Contains(normalizedSearch));
        }

        if (query.Status is not null)
        {
            source = source.Where(x => x.Ticket.Status == query.Status);
        }

        if (query.Priority is not null)
        {
            source = source.Where(x => x.Ticket.Priority == query.Priority);
        }

        if (query.SlaStatus is not null)
        {
            source = ApplySlaStatusFilter(source, query.SlaStatus.Value, now);
        }

        if (query.CategoryId is not null)
        {
            source = source.Where(x => x.Ticket.CategoryId == query.CategoryId);
        }

        if (query.CustomerId is not null)
        {
            source = source.Where(x => x.Ticket.CustomerId == query.CustomerId);
        }

        if (query.AssignedAgentId is not null)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == query.AssignedAgentId);
        }

        if (query.UnassignedOnly)
        {
            source = source.Where(x => x.Ticket.AssignedAgentId == null);
        }

        return source;
    }

    private IQueryable<TicketWithLabels> ApplySlaStatusFilter(
        IQueryable<TicketWithLabels> source,
        SlaStatus status,
        DateTime now)
    {
        if (status == SlaStatus.NotApplicable)
        {
            return source.Where(x => x.Ticket.DueAtUtc == null);
        }

        if (status == SlaStatus.Met)
        {
            return source.Where(x => x.Ticket.DueAtUtc != null
                && x.Ticket.ResolvedAtUtc != null
                && x.Ticket.ResolvedAtUtc <= x.Ticket.DueAtUtc);
        }

        if (status == SlaStatus.Breached)
        {
            return source.Where(x => x.Ticket.DueAtUtc != null
                && ((x.Ticket.ResolvedAtUtc != null && x.Ticket.ResolvedAtUtc > x.Ticket.DueAtUtc)
                    || (x.Ticket.ResolvedAtUtc == null && now > x.Ticket.DueAtUtc)));
        }

        if (status == SlaStatus.AtRisk)
        {
            var riskWindow = BuildAtRiskWindowPredicate(now);
            return source.Where(x => x.Ticket.DueAtUtc != null
                && x.Ticket.ResolvedAtUtc == null
                && x.Ticket.DueAtUtc >= now)
                .Where(riskWindow);
        }

        // At this point only unresolved tickets that are neither past due nor AtRisk qualify.
        var outsideRiskWindow = BuildAtRiskWindowPredicate(now, negate: true);
        return source.Where(x => x.Ticket.DueAtUtc != null
            && x.Ticket.ResolvedAtUtc == null
            && x.Ticket.DueAtUtc > now)
            .Where(outsideRiskWindow);
    }

    private Expression<Func<TicketWithLabels, bool>> BuildAtRiskWindowPredicate(
        DateTime now,
        bool negate = false)
    {
        var row = Expression.Parameter(typeof(TicketWithLabels), "x");
        var ticket = Expression.Property(row, nameof(TicketWithLabels.Ticket));
        var customer = Expression.Property(row, nameof(TicketWithLabels.Customer));
        var priority = Expression.Property(ticket, nameof(Ticket.Priority));
        var tier = Expression.Property(customer, nameof(Customer.Tier));
        var due = Expression.Property(ticket, nameof(Ticket.DueAtUtc));
        var dueValue = Expression.Property(due, nameof(Nullable<DateTime>.Value));
        Expression? body = null;

        foreach (var ticketPriority in Enum.GetValues<TicketPriority>())
        {
            foreach (var customerTier in Enum.GetValues<CustomerTier>())
            {
                var matchesWindow = Expression.AndAlso(
                    Expression.Equal(priority, Expression.Constant(ticketPriority)),
                    Expression.Equal(tier, Expression.Constant(customerTier)));
                var withinRiskCutoff = Expression.LessThanOrEqual(
                    dueValue,
                    Expression.Constant(AtRiskCutoff(ticketPriority, customerTier, now)));
                var window = Expression.AndAlso(matchesWindow, withinRiskCutoff);
                body = body is null ? window : Expression.OrElse(body, window);
            }
        }

        return Expression.Lambda<Func<TicketWithLabels, bool>>(
            negate ? Expression.Not(body!) : body!, row);
    }

    private DateTime AtRiskCutoff(TicketPriority priority, CustomerTier tier, DateTime now)
    {
        var window = EffectiveWindow(priority, tier, slaSettings);
        var thresholdTicks = (long)(window.Ticks * slaSettings.AtRiskThresholdPercent / 100);
        return now.AddTicks(thresholdTicks);
    }

    private static TimeSpan EffectiveWindow(
        TicketPriority priority,
        CustomerTier tier,
        SlaPolicySettings settings)
    {
        var baseHours = settings.BaseWindowHours[priority];
        var effectiveHours = tier == CustomerTier.Premium
            ? Math.Max(baseHours * settings.PremiumCustomerMultiplier, settings.MinimumWindowHours)
            : baseHours;
        return TimeSpan.FromHours(effectiveHours);
    }

    private static SlaStatus EvaluateStatus(
        TicketPriority priority,
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        CustomerTier tier,
        DateTime now,
        SlaPolicySettings settings) =>
        SlaEvaluator.Evaluate(
            dueAtUtc,
            resolvedAtUtc,
            now,
            EffectiveWindow(priority, tier, settings),
            settings.AtRiskThresholdPercent);

    private static IQueryable<TicketWithLabels> ApplySort(IQueryable<TicketWithLabels> source, TicketQuery query)
    {
        var descending = !string.Equals(query.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        var sorted = query.SortBy?.ToLowerInvariant() switch
        {
            "updatedatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.UpdatedAtUtc)
                : source.OrderBy(x => x.Ticket.UpdatedAtUtc),
            "dueatutc" => descending
                ? source.OrderByDescending(x => x.Ticket.DueAtUtc)
                : source.OrderBy(x => x.Ticket.DueAtUtc),
            "priority" => descending
                ? source.OrderByDescending(x => x.Ticket.Priority)
                : source.OrderBy(x => x.Ticket.Priority),
            "status" => descending
                ? source.OrderByDescending(x => x.Ticket.Status)
                : source.OrderBy(x => x.Ticket.Status),
            _ => descending
                ? source.OrderByDescending(x => x.Ticket.CreatedAtUtc)
                : source.OrderBy(x => x.Ticket.CreatedAtUtc)
        };

        // Keeps paging stable when the sort column has duplicates.
        return sorted.ThenByDescending(x => x.Ticket.Id);
    }

    private sealed class TicketWithLabels
    {
        public required Ticket Ticket { get; init; }

        public required Customer Customer { get; init; }

        public required Category Category { get; init; }

        public Agent? Agent { get; init; }
    }
}

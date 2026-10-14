using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.BusinessRules;

public sealed class SlaCalculator(SlaPolicySettings settings)
{
    public SlaCalculation Calculate(
        TicketPriority priority,
        CustomerTier customerTier,
        DateTime fromUtc)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var baseHours = settings.BaseWindowHours[priority];
        var effectiveHours = customerTier == CustomerTier.Premium
            ? Math.Max(baseHours * settings.PremiumCustomerMultiplier, settings.MinimumWindowHours)
            : baseHours;
        var effectiveWindow = TimeSpan.FromHours(effectiveHours);

        var reason = customerTier == CustomerTier.Premium
            ? $"{priority} has a {baseHours:0.##}-hour base SLA; Premium customer adjustment gives "
              + $"{effectiveHours:0.##} hours."
            : $"{priority} has a configured {effectiveHours:0.##}-hour SLA window.";

        return new SlaCalculation(effectiveWindow, fromUtc.Add(effectiveWindow), reason);
    }
}

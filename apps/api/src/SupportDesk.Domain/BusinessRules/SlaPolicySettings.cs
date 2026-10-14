using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Domain.BusinessRules;

public sealed class SlaPolicySettings
{
    public Dictionary<TicketPriority, double> BaseWindowHours { get; init; } = new()
    {
        [TicketPriority.Low] = 72,
        [TicketPriority.Medium] = 24,
        [TicketPriority.High] = 8,
        [TicketPriority.Critical] = 4
    };

    public double PremiumCustomerMultiplier { get; init; } = 0.5;

    public double MinimumWindowHours { get; init; } = 1;

    public double AtRiskThresholdPercent { get; init; } = 25;

    public void Validate()
    {
        foreach (var priority in Enum.GetValues<TicketPriority>())
        {
            if (!BaseWindowHours.TryGetValue(priority, out var hours)
                || !double.IsFinite(hours)
                || hours <= 0)
            {
                throw new InvalidOperationException(
                    $"SLA base window for {priority} must be greater than zero.");
            }
        }

        if (!double.IsFinite(PremiumCustomerMultiplier)
            || PremiumCustomerMultiplier <= 0
            || PremiumCustomerMultiplier > 1)
        {
            throw new InvalidOperationException(
                "SLA premium customer multiplier must be greater than zero and no greater than one.");
        }

        if (!double.IsFinite(MinimumWindowHours) || MinimumWindowHours <= 0)
        {
            throw new InvalidOperationException("SLA minimum window must be greater than zero.");
        }

        if (!double.IsFinite(AtRiskThresholdPercent)
            || AtRiskThresholdPercent < 0
            || AtRiskThresholdPercent > 100)
        {
            throw new InvalidOperationException("SLA at-risk threshold must be between zero and 100.");
        }
    }
}

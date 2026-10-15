namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// Works out where a ticket stands against its due date. SLA status is always derived from
/// the ticket's dates and the current time - never stored.
/// </summary>
public static class SlaEvaluator
{
    public static SlaStatus Evaluate(DateTime? dueAtUtc, DateTime? resolvedAtUtc, DateTime nowUtc) =>
        Evaluate(dueAtUtc, resolvedAtUtc, nowUtc, null, 25);

    public static SlaStatus Evaluate(
        DateTime? dueAtUtc,
        DateTime? resolvedAtUtc,
        DateTime nowUtc,
        TimeSpan? effectiveWindow,
        double atRiskThresholdPercent)
    {
        if (dueAtUtc is null)
        {
            return SlaStatus.NotApplicable;
        }

        if (resolvedAtUtc is not null)
        {
            return resolvedAtUtc <= dueAtUtc ? SlaStatus.Met : SlaStatus.Breached;
        }

        if (nowUtc > dueAtUtc)
        {
            return SlaStatus.Breached;
        }

        if (effectiveWindow is not null
            && effectiveWindow > TimeSpan.Zero
            && nowUtc.Add(effectiveWindow.Value) >= dueAtUtc.Value)
        {
            var remaining = dueAtUtc.Value - nowUtc;
            var threshold = TimeSpan.FromTicks(
                (long)(effectiveWindow.Value.Ticks * atRiskThresholdPercent / 100));

            if (remaining <= threshold)
            {
                return SlaStatus.AtRisk;
            }
        }

        return SlaStatus.WithinSla;
    }
}

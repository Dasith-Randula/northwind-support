using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain.Aggregates.Tickets;

public sealed class SlaEvaluatorAtRiskTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;
    private static readonly TimeSpan Window = TimeSpan.FromHours(8);

    [Fact]
    public void Evaluate_UnresolvedBeforeAtRiskWindow_IsWithinSla() =>
        Assert.Equal(
            SlaStatus.WithinSla,
            SlaEvaluator.Evaluate(Now.AddHours(8), null, Now, Window, 25));

    [Fact]
    public void Evaluate_UnresolvedAtThreshold_IsAtRisk() =>
        Assert.Equal(
            SlaStatus.AtRisk,
            SlaEvaluator.Evaluate(Now.AddHours(2), null, Now, Window, 25));

    [Fact]
    public void Evaluate_UnresolvedInsideAtRiskWindow_IsAtRisk() =>
        Assert.Equal(
            SlaStatus.AtRisk,
            SlaEvaluator.Evaluate(Now.AddHours(1), null, Now, Window, 25));

    [Fact]
    public void Evaluate_UnresolvedAtDueTime_IsBreached() =>
        Assert.Equal(
            SlaStatus.Breached,
            SlaEvaluator.Evaluate(Now, null, Now, Window, 25));

    [Fact]
    public void Evaluate_UnresolvedAfterDueTime_IsBreached() =>
        Assert.Equal(
            SlaStatus.Breached,
            SlaEvaluator.Evaluate(Now.AddHours(-1), null, Now, Window, 25));

    [Fact]
    public void Evaluate_ResolvedAtDueTime_IsMet()
    {
        var due = Now.AddHours(8);

        Assert.Equal(SlaStatus.Met, SlaEvaluator.Evaluate(due, due, Now));
    }

    [Fact]
    public void Evaluate_ResolvedOneTickAfterDueTime_IsBreached()
    {
        var due = Now.AddHours(8);

        Assert.Equal(SlaStatus.Breached, SlaEvaluator.Evaluate(due, due.AddTicks(1), Now));
    }

    [Fact]
    public void Evaluate_UsesConfiguredAtRiskThreshold()
    {
        Assert.Equal(
            SlaStatus.AtRisk,
            SlaEvaluator.Evaluate(Now.AddHours(4), null, Now, Window, 50));
        Assert.Equal(
            SlaStatus.WithinSla,
            SlaEvaluator.Evaluate(Now.AddHours(4), null, Now, Window, 25));
    }

    [Fact]
    public void Evaluate_UsesEffectivePremiumWindow()
    {
        var premiumWindow = TimeSpan.FromHours(4);

        Assert.Equal(
            SlaStatus.AtRisk,
            SlaEvaluator.Evaluate(Now.AddHours(1), null, Now, premiumWindow, 25));
    }
}

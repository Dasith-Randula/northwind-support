namespace SupportDesk.Domain.BusinessRules;

public sealed record SlaCalculation(TimeSpan EffectiveWindow, DateTime DueAtUtc, string Reason);

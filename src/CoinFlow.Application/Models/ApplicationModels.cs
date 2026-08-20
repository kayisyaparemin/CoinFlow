using CoinFlow.Domain.Calculations;

namespace CoinFlow.Application.Models;

public sealed record DashboardSnapshot(
    SalaryPeriodProjection CurrentPeriod,
    IReadOnlyList<ObligationItem> UpcomingPayments,
    decimal TwelvePeriodEndingProjectedSavings,
    SalaryPeriodProjection TightestPeriod,
    bool HasUndeterminedCardPayments);

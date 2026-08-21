using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record CreditCardPaymentProjectionStatus(
    Guid CardId,
    string CardName,
    DateOnly StatementCloseDate,
    DateOnly PaymentDueDate,
    decimal? StatementBalance,
    decimal? MinimumPayment,
    decimal? Payment,
    decimal? OpeningCarriedBalance,
    decimal NewCharges,
    decimal? CarriedPrincipalAfterPayment,
    decimal CarryInterest,
    decimal? NextCarriedBalance,
    decimal AppliedInterestRate,
    CreditCardPaymentResolution Resolution,
    CreditCardPaymentType? PaymentType,
    DateOnly AssignedSalaryDate = default,
    bool PaymentBeforeSalary = false,
    PaymentAssignmentMode? ActiveMode = null,
    PaymentAssignmentReason? AssignmentReason = null,
    bool IsPreFirstSalaryObligation = false);

public sealed record SalaryPeriodProjection(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal SalaryIncome,
    decimal OtherIncome,
    decimal TotalIncome,
    decimal LoanPayments,
    decimal CreditCardPayments,
    decimal TemporaryPayments,
    decimal InstallmentPayments,
    decimal OtherScheduledPayments,
    decimal MandatoryOutflow,
    decimal AvailableAfterMandatory,
    decimal LivingBudget,
    decimal EstimatedSavingsCapacity,
    decimal PlannedLargeCashExpenses,
    decimal OpeningProjectedSavings,
    decimal EndingProjectedSavings,
    bool IsEstimatedCardPayment,
    bool HasUndeterminedCardPayment,
    bool HasDeficit,
    IReadOnlyList<IncomeProjectionItem> IncomeItems,
    IReadOnlyList<ObligationItem> MandatoryItems,
    IReadOnlyList<PlannedLargeExpense> LargeExpenseItems,
    IReadOnlyList<CreditCardPaymentProjectionStatus> CardPaymentStatuses,
    PaymentAssignmentMode PaymentAssignmentMode =
        PaymentAssignmentMode.UpcomingPeriod,
    DateOnly PaymentWindowStart = default,
    DateOnly PaymentWindowEnd = default,
    bool IsStrategyTransition = false,
    bool IsInitialSnapshotPeriod = false,
    decimal NormalMandatoryAmount = 0m,
    decimal TransitionCatchUpAmount = 0m,
    decimal ForwardFundedAmount = 0m,
    DateOnly ProjectionAnchorDate = default,
    decimal EndingProjectedSavingsBeforeDeficitInterest = 0m,
    decimal DeficitFinancingInterest = 0m,
    decimal CardInterestGenerated = 0m,
    decimal AppliedDeficitInterestRate = 0m)
{
    public SalaryPeriod Period => new(PeriodStart, PeriodEnd);
    public decimal CarryOverDeficit => OpeningProjectedSavings < 0m
        ? Math.Abs(OpeningProjectedSavings)
        : 0m;
    public decimal AvailableAfterCarryOverDeficit =>
        AvailableAfterMandatory - CarryOverDeficit;
    public decimal CurrentPeriodNetContribution =>
        EstimatedSavingsCapacity;
    public decimal CurrentPeriodSavingsBeforeDeficitInterest =>
        EstimatedSavingsCapacity;
    public decimal DeficitPrincipal =>
        EndingProjectedSavingsBeforeDeficitInterest < 0m
            ? Math.Abs(EndingProjectedSavingsBeforeDeficitInterest)
            : 0m;
    public decimal TotalInterestGenerated =>
        CardInterestGenerated + DeficitFinancingInterest;
    public decimal DeficitCoveredThisPeriod => CarryOverDeficit == 0m
        ? 0m
        : Math.Min(
            CarryOverDeficit,
            Math.Max(0m, CurrentPeriodNetContribution));
    public decimal RemainingCarryOverDeficit => EndingProjectedSavings < 0m
        ? Math.Abs(EndingProjectedSavings)
        : 0m;
    public bool HasCarryOverDeficit => CarryOverDeficit > 0m;
    public bool RecoveredCarryOverDeficit =>
        HasCarryOverDeficit && EndingProjectedSavings >= 0m;
}

public sealed record FinancialProjectionResult(
    IReadOnlyList<SalaryPeriodProjection> Periods,
    SalaryFundingPlan FundingPlan,
    IReadOnlyList<CreditCardPaymentProjectionStatus> CardPaymentStatuses)
{
    public decimal TotalCreditCardInterest =>
        Periods.Sum(x => x.CardInterestGenerated);
    public decimal TotalDeficitFinancingInterest =>
        Periods.Sum(x => x.DeficitFinancingInterest);
    public decimal TotalInterestCost =>
        TotalCreditCardInterest + TotalDeficitFinancingInterest;
}

public sealed record ProjectionInterestSummary(
    decimal CreditCardInterest,
    decimal DeficitFinancingInterest)
{
    public decimal TotalInterestCost =>
        CreditCardInterest + DeficitFinancingInterest;

    public static ProjectionInterestSummary From(
        IEnumerable<SalaryPeriodProjection> periods)
    {
        var rows = periods.ToArray();
        return new ProjectionInterestSummary(
            rows.Sum(x => x.CardInterestGenerated),
            rows.Sum(x => x.DeficitFinancingInterest));
    }
}

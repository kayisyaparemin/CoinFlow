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
    CreditCardPaymentResolution Resolution,
    CreditCardPaymentType? PaymentType);

public sealed record FutureMonthProjection(
    SalaryPeriod Period,
    decimal Salary,
    decimal LoanPayments,
    decimal CardPayments,
    decimal TemporaryPayments,
    decimal PlannedInstallments,
    decimal EmergencyFundContribution,
    decimal TotalObligations,
    decimal ProjectedSpendable,
    decimal? ActualRemaining,
    decimal ProjectedDailyCoin,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<CreditCardPaymentProjectionStatus> CardPaymentStatuses)
{
    public decimal Spendable => ActualRemaining ?? ProjectedSpendable;
    public bool IsCurrentActual => ActualRemaining is not null;
    public bool HasUndeterminedCardPayments =>
        CardPaymentStatuses.Any(x => x.Resolution == CreditCardPaymentResolution.Undetermined);
    public bool UsesCardPaymentFallback =>
        CardPaymentStatuses.Any(x => x.Resolution == CreditCardPaymentResolution.ProjectionFallback);
}

public enum PurchaseFundingMethod
{
    CreditCard = 0,
    CashDebt = 1,
    BankLoan = 2,
    Cash = 3
}

public sealed record PurchaseSimulationRequest(
    string Name,
    decimal TotalAmount,
    PurchaseFundingMethod FundingMethod,
    DateOnly PurchaseDate,
    int InstallmentCount,
    DateOnly FirstPaymentDate,
    Guid? CreditCardId = null,
    decimal? TotalRepaymentAmount = null);

public sealed record PurchaseSimulationRow(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    bool UsesCurrentActual,
    decimal BaselineObligations,
    decimal BaselineSpendable,
    decimal NewPayment,
    decimal ResultingObligations,
    decimal ResultingSpendable,
    decimal RemainingNewDebt);

public sealed record PurchaseSimulationResult(
    PurchaseFundingMethod FundingMethod,
    decimal PurchaseAmount,
    decimal TotalRepaymentAmount,
    decimal ExistingObligationsInHorizon,
    decimal NewPaymentsInHorizon,
    decimal RemainingNewDebtAfterHorizon,
    string Explanation,
    IReadOnlyList<PurchaseSimulationRow> Rows);

public sealed class PurchaseSimulationCalculator(
    CreditCardProjectionCalculator cardCalculator,
    InstallmentScheduleCalculator installmentScheduleCalculator)
{
    public PurchaseSimulationResult Calculate(
        PurchaseSimulationRequest request,
        IEnumerable<FutureMonthProjection> baseline,
        IEnumerable<CreditCard> creditCards)
    {
        if (request.TotalAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Tutar sıfırdan büyük olmalıdır.");
        }

        var baselineRows = baseline.OrderBy(x => x.Period.Start).ToArray();
        if (baselineRows.Length == 0)
        {
            throw new InvalidOperationException("Simülasyon için en az bir maaş dönemi gereklidir.");
        }

        if (request.FundingMethod != PurchaseFundingMethod.Cash &&
            request.InstallmentCount is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Taksit sayısı 1 ile 120 arasında olmalıdır.");
        }

        if (request.FundingMethod is PurchaseFundingMethod.CashDebt or PurchaseFundingMethod.BankLoan &&
            request.FirstPaymentDate < request.PurchaseDate)
        {
            throw new InvalidOperationException("İlk ödeme tarihi alışveriş tarihinden önce olamaz.");
        }

        return request.FundingMethod switch
        {
            PurchaseFundingMethod.Cash => CalculateCash(request, baselineRows),
            PurchaseFundingMethod.CreditCard => CalculateCreditCard(request, baselineRows, creditCards),
            PurchaseFundingMethod.CashDebt => CalculateRepaymentPlan(request, baselineRows, "Nakit borç"),
            PurchaseFundingMethod.BankLoan => CalculateRepaymentPlan(request, baselineRows, "Banka kredisi"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Desteklenmeyen ödeme yöntemi.")
        };
    }

    private static PurchaseSimulationResult CalculateCash(
        PurchaseSimulationRequest request,
        IReadOnlyList<FutureMonthProjection> baseline)
    {
        var purchaseInHorizon = baseline.Any(row => row.Period.Contains(request.PurchaseDate));
        var rows = baseline.Select(row =>
        {
            var payment = row.Period.Contains(request.PurchaseDate) ? request.TotalAmount : 0m;
            var remaining = purchaseInHorizon && row.Period.End > request.PurchaseDate ? 0m : request.TotalAmount;
            return CreateRow(row, payment, remaining);
        }).ToArray();

        return CreateResult(
            request,
            baseline,
            request.TotalAmount,
            "Tutar, alışveriş tarihini içeren maaş döneminde mevcutsa gerçek serbest bakiyeden; gelecek dönemdeyse tahmini serbest bütçeden tek seferde düşüldü.",
            rows);
    }

    private PurchaseSimulationResult CalculateRepaymentPlan(
        PurchaseSimulationRequest request,
        IReadOnlyList<FutureMonthProjection> baseline,
        string methodName)
    {
        var repaymentTotal = request.TotalRepaymentAmount ?? request.TotalAmount;
        if (repaymentTotal < request.TotalAmount)
        {
            throw new InvalidOperationException("Toplam geri ödeme alışveriş tutarından düşük olamaz.");
        }

        var schedule = installmentScheduleCalculator.Split(
            repaymentTotal,
            request.InstallmentCount,
            request.FirstPaymentDate);
        var rows = baseline.Select(row =>
        {
            var payment = schedule.Where(x => row.Period.Contains(x.Date)).Sum(x => x.Amount);
            var paidThroughPeriod = schedule.Where(x => x.Date < row.Period.End).Sum(x => x.Amount);
            return CreateRow(row, payment, Math.Max(0m, repaymentTotal - paidThroughPeriod));
        }).ToArray();

        var financingCost = repaymentTotal - request.TotalAmount;
        var explanation = financingCost == 0m
            ? $"{methodName}, exact ödeme tarihleriyle {request.InstallmentCount} eşit geri ödeme halinde mevcut zorunlu ödemelerin üzerine eklendi."
            : $"{methodName}, faiz ve masraflar dahil girilen toplam geri ödeme exact tarihlerle mevcut zorunlu ödemelerin üzerine eklendi.";

        return CreateResult(request, baseline, repaymentTotal, explanation, rows);
    }

    private PurchaseSimulationResult CalculateCreditCard(
        PurchaseSimulationRequest request,
        IReadOnlyList<FutureMonthProjection> baseline,
        IEnumerable<CreditCard> creditCards)
    {
        if (request.CreditCardId is null)
        {
            throw new InvalidOperationException("Kredi kartı ile simülasyon için kart seçilmelidir.");
        }

        var card = creditCards.SingleOrDefault(x => x.Id == request.CreditCardId.Value)
            ?? throw new InvalidOperationException("Seçilen kredi kartı bulunamadı.");
        var availableLimit = Math.Max(0m, card.Limit - card.CurrentTotalDebt);
        if (card.Limit > 0m && request.TotalAmount > availableLimit)
        {
            throw new InvalidOperationException("Kartın kullanılabilir limiti bu alışveriş için yetersiz.");
        }

        var simulatedCharges = installmentScheduleCalculator
            .Split(request.TotalAmount, request.InstallmentCount, request.PurchaseDate)
            .Select(x => new CardCharge
            {
                CreditCardId = card.Id,
                Description = request.Name,
                PostingDate = x.Date,
                Amount = x.Amount
            })
            .ToArray();
        var scenarioCard = card with
        {
            CurrentTotalDebt = card.CurrentTotalDebt + request.TotalAmount,
            Charges = card.Charges.Concat(simulatedCharges).ToArray()
        };

        var statementCount = Math.Max(24, baseline.Count + request.InstallmentCount + 4);
        var currentProjection = cardCalculator.Project(card, statementCount, useProjectionFallback: true);
        var scenarioProjection = cardCalculator.Project(scenarioCard, statementCount, useProjectionFallback: true);
        if (currentProjection.Zip(scenarioProjection).Any(x =>
                x.First.Payment is null || x.Second.Payment is null))
        {
            throw new InvalidOperationException(
                "Kart simülasyonu için belirlenmemiş ödemelerde bir gelecek ay tahmin varsayımı seçilmelidir.");
        }

        var paymentDeltas = scenarioProjection
            .Zip(currentProjection, (scenario, current) => new
            {
                scenario.PaymentDueDate,
                Amount = Math.Max(0m, scenario.Payment!.Value - current.Payment!.Value)
            })
            .ToArray();

        decimal cumulativePayment = 0m;
        var rows = baseline.Select(row =>
        {
            var payment = paymentDeltas.Where(x => row.Period.Contains(x.PaymentDueDate)).Sum(x => x.Amount);
            cumulativePayment += payment;
            return CreateRow(row, payment, Math.Max(0m, request.TotalAmount - cumulativePayment));
        }).ToArray();

        return CreateResult(
            request,
            baseline,
            request.TotalAmount,
            $"{card.Bank} {card.Name} için alışveriş taksitleri exact posting tarihleriyle gerçek ekstre kapanışlarına ve son ödeme tarihlerine dağıtıldı. Faiz ve vergiler dahil değildir.",
            rows);
    }

    private static PurchaseSimulationResult CreateResult(
        PurchaseSimulationRequest request,
        IReadOnlyList<FutureMonthProjection> baseline,
        decimal repaymentTotal,
        string explanation,
        IReadOnlyList<PurchaseSimulationRow> rows) => new(
            request.FundingMethod,
            request.TotalAmount,
            repaymentTotal,
            baseline.Sum(x => x.TotalObligations),
            rows.Sum(x => x.NewPayment),
            rows.Count == 0 ? repaymentTotal : rows[^1].RemainingNewDebt,
            explanation,
            rows);

    private static PurchaseSimulationRow CreateRow(
        FutureMonthProjection row,
        decimal newPayment,
        decimal remainingNewDebt) => new(
            row.Period.Start,
            row.Period.End,
            row.IsCurrentActual,
            row.TotalObligations,
            row.Spendable,
            newPayment,
            row.TotalObligations + newPayment,
            row.Spendable - newPayment,
            remainingNewDebt);
}

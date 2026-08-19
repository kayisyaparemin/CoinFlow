using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record FutureMonthProjection(
    SalaryPeriod Period,
    decimal Salary,
    decimal LoanPayments,
    decimal CardPayments,
    decimal TemporaryPayments,
    decimal PlannedInstallments,
    decimal EmergencyFundContribution,
    decimal TotalObligations,
    decimal Spendable,
    IReadOnlyList<string> Highlights);

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
    DateOnly Month,
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

public sealed class PurchaseSimulationCalculator
{
    private readonly CreditCardProjectionCalculator _cardCalculator;

    public PurchaseSimulationCalculator(CreditCardProjectionCalculator cardCalculator)
    {
        _cardCalculator = cardCalculator;
    }

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

        if (request.FundingMethod != PurchaseFundingMethod.Cash &&
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
        var rows = baseline.Select(row =>
        {
            var payment = row.Period.Contains(request.PurchaseDate) ? request.TotalAmount : 0m;
            return CreateRow(row, payment, 0m);
        }).ToArray();

        return CreateResult(
            request,
            baseline,
            request.TotalAmount,
            "Alışveriş tutarı, alışveriş tarihini içeren maaş dönemindeki kullanılabilir nakitten tek seferde düşüldü.",
            rows);
    }

    private static PurchaseSimulationResult CalculateRepaymentPlan(
        PurchaseSimulationRequest request,
        IReadOnlyList<FutureMonthProjection> baseline,
        string methodName)
    {
        var repaymentTotal = request.TotalRepaymentAmount ?? request.TotalAmount;
        if (repaymentTotal < request.TotalAmount)
        {
            throw new InvalidOperationException("Toplam geri ödeme alışveriş tutarından düşük olamaz.");
        }

        var schedule = BuildSchedule(repaymentTotal, request.InstallmentCount, request.FirstPaymentDate);
        var rows = baseline.Select(row =>
        {
            var payment = schedule.Where(x => row.Period.Contains(x.DueDate)).Sum(x => x.Amount);
            var paidThroughPeriod = schedule.Where(x => x.DueDate < row.Period.End).Sum(x => x.Amount);
            var remaining = Math.Max(0m, repaymentTotal - paidThroughPeriod);
            return CreateRow(row, payment, remaining);
        }).ToArray();

        var financingCost = repaymentTotal - request.TotalAmount;
        var explanation = financingCost == 0m
            ? $"{methodName}, {request.InstallmentCount} eşit geri ödeme halinde mevcut zorunlu ödemelerin üzerine eklendi."
            : $"{methodName}, girilen faiz ve masraflar dahil toplam geri ödeme ile mevcut zorunlu ödemelerin üzerine eklendi.";

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

        var firstDueDate = CalendarRules.ResolveDay(
            baseline[0].Period.Start.Year,
            baseline[0].Period.Start.Month,
            card.PaymentDueDay);
        if (firstDueDate < baseline[0].Period.Start)
        {
            firstDueDate = CalendarRules.AddMonthsKeepingDay(firstDueDate, 1, card.PaymentDueDay);
        }

        var purchaseSchedule = BuildSchedule(request.TotalAmount, request.InstallmentCount, request.FirstPaymentDate);
        var projectionStartMonth = new DateOnly(firstDueDate.Year, firstDueDate.Month, 1);
        var firstProjectionChargeMonth = projectionStartMonth;
        var preProjectionCharges = 0m;
        var simulatedInstallments = new List<CardInstallment>();
        foreach (var payment in purchaseSchedule)
        {
            var paymentMonth = new DateOnly(payment.DueDate.Year, payment.DueDate.Month, 1);
            var chargeMonth = paymentMonth.AddMonths(-1);
            if (chargeMonth < firstProjectionChargeMonth)
            {
                preProjectionCharges += payment.Amount;
                continue;
            }

            simulatedInstallments.Add(new CardInstallment
            {
                CreditCardId = card.Id,
                Description = request.Name,
                DueDate = CalendarRules.ResolveDay(chargeMonth.Year, chargeMonth.Month, card.StatementClosingDay),
                Amount = payment.Amount
            });
        }

        var openingBalance = card.LastStatementRemaining > 0m
            ? card.LastStatementRemaining
            : card.LastStatementDebt;
        var scenarioCard = card with
        {
            CurrentTotalDebt = card.CurrentTotalDebt + request.TotalAmount,
            LastStatementDebt = openingBalance + preProjectionCharges,
            LastStatementRemaining = openingBalance + preProjectionCharges,
            FutureInstallments = card.FutureInstallments.Concat(simulatedInstallments).ToArray()
        };

        var projectionMonthCount = Math.Max(14, baseline.Count + 2);
        var currentProjection = _cardCalculator.Project(card, firstDueDate, projectionMonthCount);
        var scenarioProjection = _cardCalculator.Project(scenarioCard, firstDueDate, projectionMonthCount);
        var paymentDeltas = scenarioProjection
            .Zip(currentProjection, (scenario, current) => new
            {
                scenario.PaymentDueDate,
                Amount = Math.Max(0m, scenario.Payment - current.Payment)
            })
            .ToArray();

        decimal cumulativePayment = 0m;
        var rows = baseline.Select(row =>
        {
            var payment = paymentDeltas.Where(x => row.Period.Contains(x.PaymentDueDate)).Sum(x => x.Amount);
            cumulativePayment += payment;
            return CreateRow(row, payment, Math.Max(0m, request.TotalAmount - cumulativePayment));
        }).ToArray();

        var paymentMode = card.PaymentMode == CreditCardPaymentMode.Manual
            ? "tanımlı manuel ilk ödeme"
            : "tanımlı asgari ödeme oranı";
        var explanation = $"{card.Bank} {card.Name} kartının güncel borcu, gelecek taksitleri ve {paymentMode} birlikte hesaplandı. Kart faizi modele dahil değildir.";

        return CreateResult(request, baseline, request.TotalAmount, explanation, rows);
    }

    private static PurchaseSimulationResult CreateResult(
        PurchaseSimulationRequest request,
        IReadOnlyList<FutureMonthProjection> baseline,
        decimal repaymentTotal,
        string explanation,
        IReadOnlyList<PurchaseSimulationRow> rows)
    {
        return new PurchaseSimulationResult(
            request.FundingMethod,
            request.TotalAmount,
            repaymentTotal,
            baseline.Sum(x => x.TotalObligations),
            rows.Sum(x => x.NewPayment),
            rows.Count == 0 ? repaymentTotal : rows[^1].RemainingNewDebt,
            explanation,
            rows);
    }

    private static PurchaseSimulationRow CreateRow(
        FutureMonthProjection row,
        decimal newPayment,
        decimal remainingNewDebt)
    {
        return new PurchaseSimulationRow(
            row.Period.Start,
            row.TotalObligations,
            row.Spendable,
            newPayment,
            row.TotalObligations + newPayment,
            row.Spendable - newPayment,
            remainingNewDebt);
    }

    private static IReadOnlyList<(DateOnly DueDate, decimal Amount)> BuildSchedule(
        decimal total,
        int count,
        DateOnly firstPaymentDate)
    {
        var regular = decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var paidBeforeLast = regular * (count - 1);
        var schedule = new List<(DateOnly DueDate, decimal Amount)>(count);
        for (var index = 0; index < count; index++)
        {
            var amount = index == count - 1 ? total - paidBeforeLast : regular;
            schedule.Add((firstPaymentDate.AddMonths(index), amount));
        }

        return schedule;
    }
}

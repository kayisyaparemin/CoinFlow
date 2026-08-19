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

public sealed record PurchaseSimulationRequest(
    string Name,
    decimal TotalAmount,
    int InstallmentCount,
    DateOnly FirstPaymentMonth);

public sealed record PurchaseSimulationRow(
    DateOnly Month,
    decimal BaselineSpendable,
    decimal NewInstallment,
    decimal ResultingSpendable);

public sealed class PurchaseSimulationCalculator
{
    public IReadOnlyList<PurchaseSimulationRow> Calculate(
        PurchaseSimulationRequest request,
        IEnumerable<FutureMonthProjection> baseline)
    {
        if (request.TotalAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Tutar sıfırdan büyük olmalıdır.");
        }

        if (request.InstallmentCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Taksit sayısı en az 1 olmalıdır.");
        }

        var regular = decimal.Round(request.TotalAmount / request.InstallmentCount, 2, MidpointRounding.AwayFromZero);
        var paidBeforeLast = regular * (request.InstallmentCount - 1);
        var firstMonth = new DateOnly(request.FirstPaymentMonth.Year, request.FirstPaymentMonth.Month, 1);

        return baseline.Select(row =>
        {
            var rowMonth = new DateOnly(row.Period.Start.Year, row.Period.Start.Month, 1);
            var index = MonthsBetween(firstMonth, rowMonth);
            var installment = index switch
            {
                < 0 => 0m,
                >= 0 when index < request.InstallmentCount - 1 => regular,
                _ when index == request.InstallmentCount - 1 => request.TotalAmount - paidBeforeLast,
                _ => 0m
            };

            return new PurchaseSimulationRow(
                rowMonth,
                row.Spendable,
                installment,
                row.Spendable - installment);
        }).ToArray();
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) =>
        ((to.Year - from.Year) * 12) + to.Month - from.Month;
}

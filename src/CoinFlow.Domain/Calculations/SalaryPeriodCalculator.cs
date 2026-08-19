using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed record SalaryPeriod(DateOnly Start, DateOnly End)
{
    public int DayCount => End.DayNumber - Start.DayNumber;
    public bool Contains(DateOnly date) => date >= Start && date < End;
}

public enum ObligationType
{
    Loan,
    TemporaryPayment,
    CreditCard,
    EmergencyFund
}

public sealed record ObligationItem(
    string Name,
    ObligationType Type,
    DateOnly DueDate,
    decimal Amount,
    bool IsFinalPayment = false);

public sealed record SalaryPeriodRequest(
    DateOnly AsOf,
    int SalaryDay,
    IReadOnlyCollection<SalaryScheduleEntry> SalarySchedule,
    IReadOnlyCollection<Loan> Loans,
    IReadOnlyCollection<TemporaryPaymentPlan> TemporaryPlans,
    IReadOnlyCollection<ObligationItem> CardPayments,
    decimal EmergencyFundContribution = 0m);

public sealed record SalaryPeriodSummary(
    SalaryPeriod Period,
    decimal Salary,
    IReadOnlyList<ObligationItem> Obligations,
    decimal TotalObligations,
    decimal SpendableBudget,
    decimal DailyCoin);

public sealed class SalaryPeriodCalculator
{
    public SalaryPeriod GetPeriod(DateOnly date, int salaryDay)
    {
        CalendarRules.ValidateDay(salaryDay);
        var currentMonthSalary = CalendarRules.ResolveDay(date.Year, date.Month, salaryDay);

        if (date >= currentMonthSalary)
        {
            return new SalaryPeriod(
                currentMonthSalary,
                CalendarRules.AddMonthsKeepingDay(currentMonthSalary, 1, salaryDay));
        }

        var previous = CalendarRules.AddMonthsKeepingDay(currentMonthSalary, -1, salaryDay);
        return new SalaryPeriod(previous, currentMonthSalary);
    }

    public decimal ResolveSalary(DateOnly periodStart, IEnumerable<SalaryScheduleEntry> schedule)
    {
        return schedule
            .Where(x => x.EffectiveFrom <= periodStart)
            .OrderByDescending(x => x.EffectiveFrom)
            .Select(x => x.NetAmount)
            .FirstOrDefault();
    }

    public SalaryPeriodSummary Calculate(SalaryPeriodRequest request)
    {
        var period = GetPeriod(request.AsOf, request.SalaryDay);
        var salary = ResolveSalary(period.Start, request.SalarySchedule);
        var obligations = new List<ObligationItem>();

        foreach (var loan in request.Loans.Where(x => x.IsActive))
        {
            var allDates = GetLoanDates(loan);
            var finalDate = allDates.Count > 0 ? allDates[^1] : (DateOnly?)null;
            obligations.AddRange(allDates
                .Where(period.Contains)
                .Select(d => new ObligationItem(
                    $"{loan.Bank} {loan.Name}".Trim(),
                    ObligationType.Loan,
                    d,
                    loan.MonthlyInstallment,
                    d == finalDate)));
        }

        foreach (var plan in request.TemporaryPlans)
        {
            var unpaid = plan.Installments.Where(x => !x.IsPaid).OrderBy(x => x.DueDate).ToArray();
            var finalDate = unpaid.LastOrDefault()?.DueDate;
            obligations.AddRange(unpaid
                .Where(x => period.Contains(x.DueDate))
                .Select(x => new ObligationItem(
                    plan.Name,
                    ObligationType.TemporaryPayment,
                    x.DueDate,
                    x.Amount,
                    x.DueDate == finalDate)));
        }

        obligations.AddRange(request.CardPayments.Where(x => period.Contains(x.DueDate)));

        if (request.EmergencyFundContribution > 0m)
        {
            obligations.Add(new ObligationItem(
                "Acil durum tamponu",
                ObligationType.EmergencyFund,
                period.Start,
                request.EmergencyFundContribution));
        }

        var ordered = obligations.OrderBy(x => x.DueDate).ThenBy(x => x.Name).ToArray();
        var total = ordered.Sum(x => x.Amount);
        var spendable = salary - total;
        var daily = period.DayCount == 0 ? 0m : decimal.Round(spendable / period.DayCount, 2, MidpointRounding.AwayFromZero);

        return new SalaryPeriodSummary(period, salary, ordered, total, spendable, daily);
    }

    public IReadOnlyList<DateOnly> GetLoanDates(Loan loan)
    {
        CalendarRules.ValidateDay(loan.PaymentDay);
        if (loan.MonthlyInstallment < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(loan), "Kredi taksiti negatif olamaz.");
        }

        var first = CalendarRules.ResolveDay(loan.StartDate.Year, loan.StartDate.Month, loan.PaymentDay);
        if (first < loan.StartDate)
        {
            first = CalendarRules.AddMonthsKeepingDay(first, 1, loan.PaymentDay);
        }

        if (loan.InstallmentCount is > 0)
        {
            return Enumerable.Range(0, loan.InstallmentCount.Value)
                .Select(i => CalendarRules.AddMonthsKeepingDay(first, i, loan.PaymentDay))
                .Where(x => loan.EndDate is null || x <= loan.EndDate.Value)
                .ToArray();
        }

        if (loan.EndDate is null || loan.EndDate < first)
        {
            return [];
        }

        var dates = new List<DateOnly>();
        for (var date = first; date <= loan.EndDate; date = CalendarRules.AddMonthsKeepingDay(date, 1, loan.PaymentDay))
        {
            dates.Add(date);
        }

        return dates;
    }
}

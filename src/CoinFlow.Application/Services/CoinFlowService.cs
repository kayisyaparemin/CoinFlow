using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed class CoinFlowService
{
    private readonly ICoinFlowStore _store;
    private readonly IClock _clock;
    private readonly SalaryPeriodCalculator _salaryCalculator;
    private readonly DailyCoinCalculator _dailyCoinCalculator;
    private readonly CreditCardProjectionCalculator _cardCalculator;
    private readonly PurchaseSimulationCalculator _simulationCalculator;

    public CoinFlowService(
        ICoinFlowStore store,
        IClock clock,
        SalaryPeriodCalculator salaryCalculator,
        DailyCoinCalculator dailyCoinCalculator,
        CreditCardProjectionCalculator cardCalculator,
        PurchaseSimulationCalculator simulationCalculator)
    {
        _store = store;
        _clock = clock;
        _salaryCalculator = salaryCalculator;
        _dailyCoinCalculator = dailyCoinCalculator;
        _cardCalculator = cardCalculator;
        _simulationCalculator = simulationCalculator;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _store.InitializeAsync(cancellationToken);

    public async Task<FinanceData> GetFinanceDataAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var settingsTask = _store.GetSettingsAsync(cancellationToken);
        var salaryTask = _store.GetSalaryScheduleAsync(cancellationToken);
        var loansTask = _store.GetLoansAsync(cancellationToken);
        var plansTask = _store.GetPaymentPlansAsync(cancellationToken);
        var cardsTask = _store.GetCreditCardsAsync(cancellationToken);
        var expensesTask = _store.GetExpensesAsync(cancellationToken: cancellationToken);
        var emergencyTask = _store.GetEmergencyFundAsync(cancellationToken);

        await Task.WhenAll(settingsTask, salaryTask, loansTask, plansTask, cardsTask, expensesTask, emergencyTask);
        return new FinanceData(
            await settingsTask,
            await salaryTask,
            await loansTask,
            await plansTask,
            await cardsTask,
            await expensesTask,
            await emergencyTask);
    }

    public async Task<DashboardSnapshot> GetDashboardAsync(
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var date = asOf ?? _clock.Today;
        var data = await GetFinanceDataAsync(cancellationToken);
        var period = _salaryCalculator.GetPeriod(date, data.Settings.SalaryDay);
        var cardPayments = BuildCardPayments(data.CreditCards, period.Start, 3);
        var salarySummary = _salaryCalculator.Calculate(new SalaryPeriodRequest(
            date,
            data.Settings.SalaryDay,
            data.Salaries,
            data.Loans,
            data.PaymentPlans,
            cardPayments,
            data.EmergencyFund.PlannedPeriodContribution));
        var dailyCoin = _dailyCoinCalculator.Calculate(
            salarySummary.Period,
            date,
            salarySummary.SpendableBudget,
            data.Expenses);
        var encouragement = CreateEncouragement(dailyCoin, data.Settings.GamificationEnabled);

        return new DashboardSnapshot(
            salarySummary,
            dailyCoin,
            data.EmergencyFund,
            data.Settings.GamificationEnabled,
            encouragement);
    }

    public async Task<IReadOnlyList<FutureMonthProjection>> GetFutureMonthsAsync(
        DateOnly? asOf = null,
        int monthCount = 12,
        CancellationToken cancellationToken = default)
    {
        if (monthCount is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(monthCount));
        }

        var date = asOf ?? _clock.Today;
        var data = await GetFinanceDataAsync(cancellationToken);
        var firstPeriod = _salaryCalculator.GetPeriod(date, data.Settings.SalaryDay);
        var horizonEnd = firstPeriod.End.AddMonths(monthCount + 1);
        var cardPayments = BuildCardPayments(data.CreditCards, firstPeriod.Start, monthCount + 2);
        var rows = new List<FutureMonthProjection>(monthCount);

        for (var i = 0; i < monthCount; i++)
        {
            var periodStart = CalendarRules.AddMonthsKeepingDay(firstPeriod.Start, i, data.Settings.SalaryDay);
            var period = new SalaryPeriod(
                periodStart,
                CalendarRules.AddMonthsKeepingDay(periodStart, 1, data.Settings.SalaryDay));
            if (period.Start >= horizonEnd)
            {
                break;
            }

            var salary = _salaryCalculator.ResolveSalary(period.Start, data.Salaries);
            var loanPayments = data.Loans.Where(x => x.IsActive)
                .SelectMany(x => _salaryCalculator.GetLoanDates(x).Select(d => (Loan: x, Date: d)))
                .Where(x => period.Contains(x.Date))
                .Sum(x => x.Loan.MonthlyInstallment);
            var temporary = data.PaymentPlans
                .Where(x => x.Kind == PaymentPlanKind.Temporary)
                .SelectMany(x => x.Installments)
                .Where(x => !x.IsPaid && period.Contains(x.DueDate))
                .Sum(x => x.Amount);
            var planned = data.PaymentPlans
                .Where(x => x.Kind == PaymentPlanKind.PlannedInstallment)
                .SelectMany(x => x.Installments)
                .Where(x => !x.IsPaid && period.Contains(x.DueDate))
                .Sum(x => x.Amount);
            var cards = cardPayments.Where(x => period.Contains(x.DueDate)).Sum(x => x.Amount);
            var buffer = data.EmergencyFund.PlannedPeriodContribution;
            var total = loanPayments + temporary + planned + cards + buffer;
            var highlights = CreateHighlights(period, data, planned + temporary);

            rows.Add(new FutureMonthProjection(
                period,
                salary,
                loanPayments,
                cards,
                temporary,
                planned,
                buffer,
                total,
                salary - total,
                highlights));
        }

        return rows;
    }

    public async Task<IReadOnlyList<PurchaseSimulationRow>> SimulatePurchaseAsync(
        PurchaseSimulationRequest request,
        DateOnly? asOf = null,
        CancellationToken cancellationToken = default)
    {
        var baseline = await GetFutureMonthsAsync(asOf, 12, cancellationToken);
        return _simulationCalculator.Calculate(request, baseline);
    }

    public async Task AddExpenseAsync(ExpenseDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), "Harcama tutarı sıfırdan büyük olmalıdır.");
        }

        var expense = new Expense
        {
            Amount = draft.Amount,
            Date = draft.Date,
            Category = draft.Category,
            PaymentType = draft.PaymentType,
            Note = draft.Note.Trim(),
            CreditCardId = draft.CreditCardId,
            InstallmentCount = draft.InstallmentCount,
            FirstInstallmentDate = draft.FirstInstallmentDate
        };

        CreditCard? targetCard = null;
        if (draft.PaymentType == ExpensePaymentType.CreditCard)
        {
            if (draft.CreditCardId is null)
            {
                throw new InvalidOperationException("Kredi kartı harcaması için kart seçilmelidir.");
            }

            targetCard = (await _store.GetCreditCardsAsync(cancellationToken))
                .SingleOrDefault(x => x.Id == draft.CreditCardId.Value)
                ?? throw new InvalidOperationException("Seçilen kredi kartı bulunamadı.");
        }

        if (draft.PaymentType == ExpensePaymentType.NewInstallment &&
            (draft.InstallmentCount.GetValueOrDefault() < 1 || draft.FirstInstallmentDate is null))
        {
            throw new InvalidOperationException("Yeni taksit için taksit sayısı ve ilk ödeme tarihi gereklidir.");
        }

        await _store.UpsertExpenseAsync(expense, cancellationToken);

        if (targetCard is not null)
        {
            await _store.UpsertCreditCardAsync(targetCard with
            {
                CurrentTotalDebt = targetCard.CurrentTotalDebt + draft.Amount,
                CurrentCycleSpending = targetCard.CurrentCycleSpending + draft.Amount
            }, cancellationToken);
        }
        else if (draft.PaymentType == ExpensePaymentType.NewInstallment)
        {
            await CreatePlanFromExpenseAsync(expense, cancellationToken);
        }
    }

    public Task SaveSalaryAsync(SalaryScheduleEntry entry, CancellationToken cancellationToken = default) =>
        _store.UpsertSalaryAsync(entry, cancellationToken);

    public Task SaveLoanAsync(Loan loan, CancellationToken cancellationToken = default) =>
        _store.UpsertLoanAsync(loan, cancellationToken);

    public Task SavePaymentPlanAsync(TemporaryPaymentPlan plan, CancellationToken cancellationToken = default) =>
        _store.UpsertPaymentPlanAsync(plan, cancellationToken);

    public Task SaveCreditCardAsync(CreditCard card, CancellationToken cancellationToken = default) =>
        _store.UpsertCreditCardAsync(card, cancellationToken);

    public Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
        _store.SaveSettingsAsync(settings, cancellationToken);

    public Task SaveEmergencyFundAsync(EmergencyFund fund, CancellationToken cancellationToken = default) =>
        _store.SaveEmergencyFundAsync(fund, cancellationToken);

    public async Task TransferToEmergencyFundAsync(decimal amount, CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        var fund = await _store.GetEmergencyFundAsync(cancellationToken);
        await _store.UpsertExpenseAsync(new Expense
        {
            Amount = amount,
            Date = _clock.Today,
            Category = ExpenseCategory.Other,
            PaymentType = ExpensePaymentType.Cash,
            Note = "Acil durum tamponuna aktarım"
        }, cancellationToken);
        await _store.SaveEmergencyFundAsync(fund with { CurrentAmount = fund.CurrentAmount + amount }, cancellationToken);
    }

    private IReadOnlyList<ObligationItem> BuildCardPayments(
        IEnumerable<CreditCard> cards,
        DateOnly fromInclusive,
        int months)
    {
        var results = new List<ObligationItem>();
        foreach (var card in cards)
        {
            var firstDue = CalendarRules.ResolveDay(fromInclusive.Year, fromInclusive.Month, card.PaymentDueDay);
            if (firstDue < fromInclusive)
            {
                firstDue = CalendarRules.AddMonthsKeepingDay(firstDue, 1, card.PaymentDueDay);
            }

            results.AddRange(_cardCalculator
                .Project(card, firstDue, months)
                .Select(x => new ObligationItem(
                    $"{card.Bank} {card.Name}".Trim(),
                    ObligationType.CreditCard,
                    x.PaymentDueDate,
                    x.Payment)));
        }

        return results;
    }

    private async Task CreatePlanFromExpenseAsync(Expense expense, CancellationToken cancellationToken)
    {
        var count = expense.InstallmentCount.GetValueOrDefault();
        var first = expense.FirstInstallmentDate;
        if (count < 1 || first is null)
        {
            throw new InvalidOperationException("Yeni taksit için taksit sayısı ve ilk ödeme tarihi gereklidir.");
        }

        var regular = decimal.Round(expense.Amount / count, 2, MidpointRounding.AwayFromZero);
        var installments = new List<TemporaryPaymentInstallment>(count);
        var planId = Guid.NewGuid();
        for (var index = 0; index < count; index++)
        {
            var amount = index == count - 1 ? expense.Amount - (regular * (count - 1)) : regular;
            installments.Add(new TemporaryPaymentInstallment
            {
                PlanId = planId,
                DueDate = first.Value.AddMonths(index),
                Amount = amount
            });
        }

        await _store.UpsertPaymentPlanAsync(new TemporaryPaymentPlan
        {
            Id = planId,
            Name = string.IsNullOrWhiteSpace(expense.Note) ? "Yeni taksit" : expense.Note,
            Kind = PaymentPlanKind.PlannedInstallment,
            Installments = installments
        }, cancellationToken);
    }

    private static string CreateEncouragement(DailyCoinSnapshot daily, bool gamified)
    {
        if (daily.TodayEarned >= 0m)
        {
            return gamified
                ? $"Bugün {daily.TodayEarned:N0} coin farm'ladın."
                : $"Bugünkü bütçenden {daily.TodayEarned:N0} TL kaldı.";
        }

        return gamified
            ? $"Bugün coin havuzundan {Math.Abs(daily.TodayEarned):N0} TL kullandın. Yarın tekrar alan açılacak."
            : $"Bugünkü harcaman günlük ortalamanın {Math.Abs(daily.TodayEarned):N0} TL üzerinde. Bütçe dönem geneline yayılır.";
    }

    private IReadOnlyList<string> CreateHighlights(SalaryPeriod period, FinanceData data, decimal plans)
    {
        var result = new List<string>();
        var finalLoans = data.Loans
            .Where(x => _salaryCalculator.GetLoanDates(x).LastOrDefault() is var last && last != default && period.Contains(last))
            .Select(x => $"{x.Name}: bu dönem son ödeme!");
        result.AddRange(finalLoans);

        var finalPlans = data.PaymentPlans
            .Where(x => x.Installments.Where(i => !i.IsPaid).OrderBy(i => i.DueDate).LastOrDefault() is var last && last is not null && period.Contains(last.DueDate))
            .Select(x => $"{x.Name}: bu dönem tamamlanıyor.");
        result.AddRange(finalPlans);

        if (plans == 0m && result.Count > 0)
        {
            result.Add("Sonraki dönemde aylık alan açılıyor.");
        }

        return result;
    }
}

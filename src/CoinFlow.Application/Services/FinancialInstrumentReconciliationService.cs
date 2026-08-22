using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Application.Services;

public sealed record ReconciledFinancialInstruments(
    IReadOnlyList<Loan> Loans,
    IReadOnlyList<TemporaryPaymentPlan> PaymentPlans,
    IReadOnlyList<CreditCard> CreditCards,
    IReadOnlyList<PlannedLargeExpense> LargeExpenses);

public sealed class FinancialInstrumentReconciliationService(
    CreditCardActualPaymentReconciler cardReconciler)
{
    public ReconciledFinancialInstruments Apply(
        FinancialPlan data,
        PeriodPlanSnapshot plan,
        IReadOnlyList<PeriodPlanPaymentLine> paymentLines,
        IReadOnlyList<ActualPayment> actualPayments,
        DateOnly newAnchor)
    {
        var lines = paymentLines.ToDictionary(x => x.Id);
        var loans = data.Loans.ToDictionary(x => x.Id);
        var paymentPlans = data.PaymentPlans.ToDictionary(x => x.Id);
        var cards = data.CreditCards.ToDictionary(x => x.Id);
        var largeExpenses = data.PlannedLargeExpenses.ToDictionary(x => x.Id);
        var unpaidLoanIds = new HashSet<Guid>();

        foreach (var actual in actualPayments.OrderBy(x => x.PlannedDate))
        {
            if (!lines.TryGetValue(
                    actual.PeriodPlanPaymentLineId,
                    out var line))
            {
                throw new InvalidOperationException(
                    "Planlanan ödeme satırı bulunamadı.");
            }

            var paid = actual.Status != ActualPaymentStatus.Unpaid &&
                       actual.ActualAmount > 0m;
            switch (actual.SourceType)
            {
                case PlanPaymentSourceType.Loan:
                    {
                        var loan = loans.GetValueOrDefault(line.SourceEntityId)
                            ?? throw new InvalidOperationException(
                                "Kredi kaydı bulunamadı.");
                        if (paid)
                        {
                            var remaining = Math.Max(
                                0,
                                loan.RemainingInstallmentCount - 1);
                            loans[loan.Id] = loan with
                            {
                                NextPaymentDate = ResolveOutstandingDate(
                                    CalendarRules.AddMonthsKeepingDay(
                                        line.PlannedDate,
                                        1,
                                        loan.PaymentDay),
                                    newAnchor),
                                RemainingInstallmentCount = remaining,
                                RemainingDebt = loan.RemainingDebt is null
                                    ? null
                                    : Math.Max(
                                        0m,
                                        loan.RemainingDebt.Value -
                                        actual.ActualAmount),
                                IsActive = remaining > 0
                            };
                        }
                        else if (loan.NextPaymentDate < newAnchor)
                        {
                            // Ödenmeyen yükümlülük gelecek plandan kaybolmaz.
                            loans[loan.Id] = loan with
                            {
                                NextPaymentDate = newAnchor
                            };
                        }
                        if (!paid)
                        {
                            unpaidLoanIds.Add(loan.Id);
                        }

                        break;
                    }

                case PlanPaymentSourceType.TemporaryPayment:
                case PlanPaymentSourceType.InstallmentPayment:
                case PlanPaymentSourceType.OtherScheduledPayment:
                    {
                        var parent = paymentPlans.Values.SingleOrDefault(x =>
                            x.Installments.Any(i => i.Id == line.SourceEntityId))
                            ?? throw new InvalidOperationException(
                                "Planlı ödeme kaydı bulunamadı.");
                        paymentPlans[parent.Id] = parent with
                        {
                            Installments = parent.Installments.Select(item =>
                                item.Id != line.SourceEntityId
                                    ? item
                                    : paid
                                        ? item with { IsPaid = true }
                                        : item.DueDate < newAnchor
                                            ? item with { DueDate = newAnchor }
                                            : item).ToArray()
                        };
                        break;
                    }

                case PlanPaymentSourceType.CreditCard:
                    {
                        var card = cards.GetValueOrDefault(line.SourceEntityId)
                            ?? throw new InvalidOperationException(
                                "Kredi kartı bulunamadı.");
                        cards[card.Id] = cardReconciler.Apply(
                            card,
                            line.PlannedDate,
                            paid ? actual.ActualAmount : 0m,
                            data.Settings.CreditCardCarryInterestRate);
                        break;
                    }

                case PlanPaymentSourceType.PlannedLargeExpense:
                    {
                        var expense = largeExpenses.GetValueOrDefault(
                            line.SourceEntityId)
                            ?? throw new InvalidOperationException(
                                "Planlı büyük ödeme bulunamadı.");
                        largeExpenses[expense.Id] = paid
                            ? expense with
                            {
                                Status = PlannedExpenseStatus.Completed
                            }
                            : expense.ExactDate < newAnchor
                                ? expense with { ExactDate = newAnchor }
                                : expense;
                        break;
                    }
            }
        }

        foreach (var loanId in unpaidLoanIds)
        {
            loans[loanId] = loans[loanId] with
            {
                NextPaymentDate = newAnchor
            };
        }

        paymentPlans = paymentPlans.ToDictionary(
            x => x.Key,
            x => x.Value with
            {
                Installments = x.Value.Installments.Select(item =>
                    !item.IsPaid && item.DueDate < newAnchor
                        ? item with { DueDate = newAnchor }
                        : item).ToArray()
            });
        largeExpenses = largeExpenses.ToDictionary(
            x => x.Key,
            x => x.Value.Status == PlannedExpenseStatus.Planned &&
                 x.Value.ExactDate < newAnchor
                ? x.Value with { ExactDate = newAnchor }
                : x.Value);

        return new ReconciledFinancialInstruments(
            loans.Values.OrderBy(x => x.NextPaymentDate).ToArray(),
            paymentPlans.Values.OrderBy(x => x.Name).ToArray(),
            cards.Values.OrderBy(x => x.Name).ToArray(),
            largeExpenses.Values.OrderBy(x => x.ExactDate).ToArray());
    }

    private static DateOnly ResolveOutstandingDate(
        DateOnly date,
        DateOnly newAnchor) => date < newAnchor ? newAnchor : date;
}

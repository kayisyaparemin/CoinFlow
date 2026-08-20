using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class ScheduledPaymentCalculator
{
    public IReadOnlyList<ObligationItem> GetItems(
        IEnumerable<TemporaryPaymentPlan> plans)
    {
        var items = new List<ObligationItem>();
        foreach (var plan in plans)
        {
            var unpaid = plan.Installments
                .Where(x => !x.IsPaid)
                .OrderBy(x => x.DueDate)
                .ToArray();
            var finalDate = unpaid.LastOrDefault()?.DueDate;
            var type = plan.Kind switch
            {
                PaymentPlanKind.Temporary => ObligationType.TemporaryPayment,
                PaymentPlanKind.Installment => ObligationType.InstallmentPayment,
                PaymentPlanKind.Recurring or PaymentPlanKind.OtherScheduled =>
                    ObligationType.OtherScheduledPayment,
                _ => throw new ArgumentOutOfRangeException(nameof(plan.Kind))
            };
            items.AddRange(unpaid
                .Select(x => new ObligationItem(
                    plan.Name,
                    type,
                    x.DueDate,
                    x.Amount,
                    x.DueDate == finalDate)));
        }

        return items;
    }
}

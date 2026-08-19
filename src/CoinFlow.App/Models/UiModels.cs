using System.Globalization;
using CoinFlow.Domain.Models;

namespace CoinFlow.App.Models;

public sealed record SelectionOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public enum CommitmentKind
{
    Salary,
    Loan,
    PaymentPlan,
    CreditCard
}

public sealed record CommitmentSummaryLine(
    Guid Id,
    CommitmentKind Kind,
    string Title,
    string Subtitle,
    string Amount,
    string Badge = "")
{
    public bool CanEdit => Kind == CommitmentKind.CreditCard;
}

public sealed record DatedAmountLine(Guid Id, DateOnly Date, decimal Amount)
{
    public string DateText => Date.ToString("dd.MM.yyyy");

    public string AmountText => $"{Amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL";
}

public sealed record CardPaymentPlanLine(
    Guid Id,
    DateOnly DueDate,
    CreditCardPaymentType PaymentType,
    decimal? Amount)
{
    public string DateText => DueDate.ToString("dd.MM.yyyy");

    public string PaymentText => PaymentType switch
    {
        CreditCardPaymentType.Minimum => "Asgari ödeme",
        CreditCardPaymentType.FullStatement => "Ekstrenin tamamı",
        CreditCardPaymentType.FixedAmount =>
            $"{Amount.GetValueOrDefault().ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL",
        _ => "—"
    };
}

public sealed record ProjectionLine(
    string Period,
    string Salary,
    string Obligations,
    string ProjectedSpendable,
    string ActualRemaining,
    bool HasActualRemaining,
    string ProjectedDailyCoin,
    string Breakdown,
    string Highlight);

public sealed record SimulationLine(
    string Month,
    string CurrentObligations,
    string CurrentSpendable,
    string NewPayment,
    string ResultingObligations,
    string ResultingSpendable,
    string RemainingDebt);

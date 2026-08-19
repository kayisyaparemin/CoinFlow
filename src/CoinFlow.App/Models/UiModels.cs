namespace CoinFlow.App.Models;

public sealed record SelectionOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public sealed record SummaryLine(string Title, string Subtitle, string Amount, string Badge = "");

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

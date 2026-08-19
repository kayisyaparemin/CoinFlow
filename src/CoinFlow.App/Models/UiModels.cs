namespace CoinFlow.App.Models;

public sealed record SelectionOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public sealed record SummaryLine(string Title, string Subtitle, string Amount, string Badge = "");

public sealed record ProjectionLine(
    string Month,
    string Salary,
    string Obligations,
    string Spendable,
    string Breakdown,
    string Highlight);

public sealed record SimulationLine(string Month, string Baseline, string Installment, string Result);

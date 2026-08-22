using CoinFlow.Domain.Calculations;

namespace CoinFlow.App.Models;

public sealed record SimulationDraftConditionView(
    Guid Id,
    SimulationRequest Request,
    string DateText,
    string TypeText,
    string SummaryText);

using CoinFlow.Domain.Models;

namespace CoinFlow.Domain.Calculations;

public sealed class SalaryResolver
{
    public SalaryScheduleEntry? Resolve(
        DateOnly periodStart,
        IEnumerable<SalaryScheduleEntry> schedule) => schedule
        .Where(x => x.EffectiveDate <= periodStart)
        .OrderByDescending(x => x.EffectiveDate)
        .ThenByDescending(x => x.Id)
        .FirstOrDefault();
}


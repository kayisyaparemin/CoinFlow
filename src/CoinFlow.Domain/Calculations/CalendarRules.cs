namespace CoinFlow.Domain.Calculations;

public static class CalendarRules
{
    public static DateOnly ResolveDay(int year, int month, int preferredDay)
    {
        ValidateDay(preferredDay);
        return new DateOnly(year, month, Math.Min(preferredDay, DateTime.DaysInMonth(year, month)));
    }

    public static DateOnly AddMonthsKeepingDay(DateOnly date, int months, int preferredDay) =>
        ResolveDay(date.AddMonths(months).Year, date.AddMonths(months).Month, preferredDay);

    public static IEnumerable<DateOnly> MonthlyDates(
        DateOnly startInclusive,
        DateOnly endExclusive,
        int preferredDay)
    {
        ValidateDay(preferredDay);
        var month = new DateOnly(startInclusive.Year, startInclusive.Month, 1);
        var finalMonth = new DateOnly(endExclusive.Year, endExclusive.Month, 1);

        while (month <= finalMonth)
        {
            var candidate = ResolveDay(month.Year, month.Month, preferredDay);
            if (candidate >= startInclusive && candidate < endExclusive)
            {
                yield return candidate;
            }

            month = month.AddMonths(1);
        }
    }

    public static void ValidateDay(int day)
    {
        if (day is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(day), "Gün 1 ile 31 arasında olmalıdır.");
        }
    }
}

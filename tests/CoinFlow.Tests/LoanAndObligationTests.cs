using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class LoanAndObligationTests
{
    [Fact]
    public void LoanSchedule_StartsOnExactNextPaymentDate()
    {
        var dates = new LoanScheduleCalculator().GetPaymentDates(new Loan
        {
            MonthlyPayment = 1_000m,
            PaymentDay = 18,
            NextPaymentDate = new DateOnly(2026, 9, 20),
            RemainingInstallmentCount = 3
        });

        Assert.Equal(new DateOnly(2026, 9, 20), dates[0]);
        Assert.Equal(new DateOnly(2026, 10, 18), dates[1]);
        Assert.Equal(new DateOnly(2026, 11, 18), dates[2]);
    }

    [Fact]
    public void LoanSchedule_UsesMonthEndAndRestoresPaymentDay()
    {
        var dates = new LoanScheduleCalculator().GetPaymentDates(new Loan
        {
            MonthlyPayment = 1_000m,
            PaymentDay = 31,
            NextPaymentDate = new DateOnly(2027, 1, 31),
            RemainingInstallmentCount = 3
        });

        Assert.Equal(
            [
                new DateOnly(2027, 1, 31),
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 3, 31)
            ],
            dates);
    }

    [Fact]
    public void LoanSchedule_StopsAtRemainingInstallmentCount()
    {
        var dates = new LoanScheduleCalculator().GetPaymentDates(new Loan
        {
            MonthlyPayment = 1_000m,
            PaymentDay = 7,
            NextPaymentDate = new DateOnly(2026, 9, 7),
            RemainingInstallmentCount = 2,
            RemainingDebt = 999_999m
        });

        Assert.Equal(2, dates.Count);
    }

    [Theory]
    [InlineData("2026-08-10", 0)]
    [InlineData("2026-09-10", 28167.40)]
    [InlineData("2026-10-10", 28167.40)]
    [InlineData("2026-11-10", 55492.20)]
    [InlineData("2026-12-10", 0)]
    public void Eminevim_MapsByExactPaymentDate(
        string periodStartText,
        double expected)
    {
        var start = DateOnly.Parse(periodStartText);
        var period = new SalaryPeriod(start, start.AddMonths(1));
        var items = new ScheduledPaymentCalculator().GetItems(
            [TestFactory.EminevimPlan()])
            .Where(x => period.Contains(x.DueDate));

        Assert.Equal((decimal)expected, items.Sum(x => x.Amount));
    }

    [Fact]
    public void MandatoryPayment_CalculatesEveryCategory()
    {
        var period = new SalaryPeriod(
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 10, 10));
        var planId = Guid.NewGuid();
        var plans = new[]
        {
            Plan(planId, PaymentPlanKind.Temporary, 2_000m),
            Plan(Guid.NewGuid(), PaymentPlanKind.Installment, 3_000m),
            Plan(Guid.NewGuid(), PaymentPlanKind.OtherScheduled, 4_000m)
        };
        var calculator = new MandatoryPaymentCalculator(
            new LoanScheduleCalculator(),
            new ScheduledPaymentCalculator(),
            new PaymentAssignmentResolver(
                new SalaryPeriodCalculator()));

        var result = calculator.Calculate(
            period,
            [
                new Loan
                {
                    MonthlyPayment = 1_000m,
                    PaymentDay = 18,
                    NextPaymentDate = new DateOnly(2026, 9, 18),
                    RemainingInstallmentCount = 1
                }
            ],
            plans,
            [
                new ObligationItem(
                    "Kart", ObligationType.CreditCard,
                    new DateOnly(2026, 10, 5), 5_000m)
            ],
            10,
            PaymentAssignmentMode.UpcomingPeriod);

        Assert.Equal(1_000m, result.LoanPayments);
        Assert.Equal(5_000m, result.CreditCardPayments);
        Assert.Equal(2_000m, result.TemporaryPayments);
        Assert.Equal(3_000m, result.InstallmentPayments);
        Assert.Equal(4_000m, result.OtherScheduledPayments);
        Assert.Equal(15_000m, result.Total);
    }

    [Fact]
    public void InstallmentSplit_PreservesExactTotalAndCount()
    {
        var schedule = new InstallmentScheduleCalculator().Split(
            120_000m,
            9,
            new DateOnly(2026, 12, 20));

        Assert.Equal(9, schedule.Count);
        Assert.Equal(120_000m, schedule.Sum(x => x.Amount));
        Assert.Equal(new DateOnly(2026, 12, 20), schedule[0].Date);
        Assert.Equal(new DateOnly(2027, 8, 20), schedule[^1].Date);
    }

    [Fact]
    public void InstallmentRounding_DeltaIsLeftToLastPayment()
    {
        var schedule = new InstallmentScheduleCalculator().Split(
            100m,
            3,
            new DateOnly(2026, 12, 20));

        Assert.Equal(33.33m, schedule[0].Amount);
        Assert.Equal(33.33m, schedule[1].Amount);
        Assert.Equal(33.34m, schedule[2].Amount);
        Assert.Equal(100m, schedule.Sum(x => x.Amount));
    }

    private static TemporaryPaymentPlan Plan(
        Guid planId,
        PaymentPlanKind kind,
        decimal amount) => new()
    {
        Id = planId,
        Name = kind.ToString(),
        Kind = kind,
        Installments =
        [
            new TemporaryPaymentInstallment
            {
                PlanId = planId,
                DueDate = new DateOnly(2026, 9, 20),
                Amount = amount
            }
        ]
    };
}

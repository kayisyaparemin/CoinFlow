using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class CreditCardStatementTests
{
    private readonly CreditCardStatementCalculator _calculator = new();

    [Fact]
    public void NewCard_DefaultsToAskEachStatement()
    {
        Assert.Equal(
            CreditCardPaymentStrategy.AskEachStatement,
            new CreditCard().PaymentStrategy);
    }

    [Fact]
    public void September24Charge_EntersSeptember25Statement()
    {
        var card = Card() with
        {
            BalanceAsOfDate = new DateOnly(2026, 8, 26),
            Charges = [Charge(new DateOnly(2026, 9, 24), 1_000m)]
        };

        var statement = _calculator.Project(card, 2)
            .Single(x =>
                x.StatementCloseDate == new DateOnly(2026, 9, 25));

        Assert.Equal(1_000m, statement.NewCharges);
    }

    [Fact]
    public void September28Charge_EntersOctober25Statement()
    {
        var card = Card() with
        {
            BalanceAsOfDate = new DateOnly(2026, 8, 26),
            Charges = [Charge(new DateOnly(2026, 9, 28), 1_000m)]
        };

        var statements = _calculator.Project(card, 3);

        Assert.Equal(
            0m,
            statements.Single(x =>
                x.StatementCloseDate == new DateOnly(2026, 9, 25))
                .NewCharges);
        Assert.Equal(
            1_000m,
            statements.Single(x =>
                x.StatementCloseDate == new DateOnly(2026, 10, 25))
                .NewCharges);
    }

    [Fact]
    public void ClosingDayAtMonthEnd_UsesRealCalendar()
    {
        var close = CreditCardStatementCalculator
            .ResolveStatementCloseOnOrAfter(
                new DateOnly(2027, 2, 1),
                31);

        Assert.Equal(new DateOnly(2027, 2, 28), close);
    }

    [Fact]
    public void SeptemberStatement_IsDueOctoberFifth()
    {
        var due = CreditCardStatementCalculator.ResolvePaymentDueDate(
            new DateOnly(2026, 9, 25),
            5);

        Assert.Equal(new DateOnly(2026, 10, 5), due);
        Assert.True(new SalaryPeriod(
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 10, 10)).Contains(due));
    }

    [Fact]
    public void CarriedPlusUnbilled_DrivesStatementAndMinimum()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 35_000m,
                UnbilledSpending = 59_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1));

        Assert.Equal(94_000m, statement.StatementBalance);
        Assert.Equal(37_600m, statement.MinimumPayment);
        Assert.Equal(37_600m, statement.Payment);
        Assert.Equal(56_400m, statement.CarriedAfterPayment);
        Assert.Equal(2_820m, statement.CarryInterest);
        Assert.Equal(59_220m, statement.NextCarriedBalance);
    }

    [Fact]
    public void MinimumPayment_UsesAwayFromZeroRounding()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 10.0125m,
                MinimumPaymentRate = 0.40m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1));

        Assert.Equal(4.01m, statement.MinimumPayment);
    }

    [Fact]
    public void FullStatement_LeavesNoCarriedBalance()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 94_000m,
                PaymentStrategy = CreditCardPaymentStrategy.FullStatement
            },
            1));

        Assert.Equal(94_000m, statement.Payment);
        Assert.Equal(0m, statement.CarriedAfterPayment);
        Assert.Equal(0m, statement.CarryInterest);
        Assert.Equal(0m, statement.NextCarriedBalance);
    }

    [Fact]
    public void MinimumPayment_AddsFivePercentCarryInterest()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 100_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1,
            carryInterestRate: 0.05m));

        Assert.Equal(100_000m, statement.StatementBalance);
        Assert.Equal(40_000m, statement.Payment);
        Assert.Equal(60_000m, statement.CarriedAfterPayment);
        Assert.Equal(3_000m, statement.CarryInterest);
        Assert.Equal(63_000m, statement.NextCarriedBalance);
        Assert.Equal(0.05m, statement.AppliedInterestRate);
    }

    [Fact]
    public void CarryInterest_CompoundsIntoNextStatement()
    {
        var statements = _calculator.Project(
            Card() with
            {
                CarriedBalance = 100_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            2,
            carryInterestRate: 0.05m);

        Assert.Equal(63_000m, statements[0].NextCarriedBalance);
        Assert.Equal(63_000m, statements[1].StatementBalance);
        Assert.Equal(25_200m, statements[1].Payment);
        Assert.Equal(37_800m, statements[1].CarriedAfterPayment);
        Assert.Equal(1_890m, statements[1].CarryInterest);
        Assert.Equal(39_690m, statements[1].NextCarriedBalance);
    }

    [Fact]
    public void ZeroInterestRate_DoesNotIncreaseCarry()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 100_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum
            },
            1,
            carryInterestRate: 0m));

        Assert.Equal(60_000m, statement.CarriedAfterPayment);
        Assert.Equal(0m, statement.CarryInterest);
        Assert.Equal(60_000m, statement.NextCarriedBalance);
    }

    [Fact]
    public void InvalidInterestRate_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Project(Card(), 1, carryInterestRate: -0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Project(Card(), 1, carryInterestRate: 1.01m));
    }

    [Theory]
    [InlineData(50000, 50000)]
    [InlineData(20000, 37600)]
    public void FixedAmount_NeverFallsBelowMinimum(
        double fixedAmount,
        double expectedPayment)
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 35_000m,
                UnbilledSpending = 59_000m,
                PaymentStrategy = CreditCardPaymentStrategy.FixedAmount,
                FixedPaymentAmount = (decimal)fixedAmount
            },
            1));

        Assert.Equal((decimal)expectedPayment, statement.Payment);
    }

    [Fact]
    public void DueDateOverride_HasPriorityOverGlobalStrategy()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with
            {
                CarriedBalance = 94_000m,
                PaymentStrategy = CreditCardPaymentStrategy.Minimum,
                PaymentPlans =
                [
                    new CreditCardPaymentPlan
                    {
                        DueDate = new DateOnly(2026, 9, 5),
                        PaymentType = CreditCardPaymentType.FixedAmount,
                        Amount = 50_000m
                    }
                ]
            },
            1));

        Assert.Equal(50_000m, statement.Payment);
        Assert.Equal(
            CreditCardPaymentResolution.DueDateOverride,
            statement.PaymentResolution);
    }

    [Fact]
    public void ProjectionFallback_IsEstimateAndDoesNotCreatePlan()
    {
        var card = Card() with
        {
            CarriedBalance = 94_000m,
            ProjectionFallbackStrategy =
                ProjectionFallbackStrategy.Minimum
        };

        var statement = Assert.Single(
            _calculator.Project(card, 1, useProjectionFallback: true));

        Assert.Equal(37_600m, statement.Payment);
        Assert.Equal(
            CreditCardPaymentResolution.ProjectionFallback,
            statement.PaymentResolution);
        Assert.Empty(card.PaymentPlans);
        Assert.Equal(
            CreditCardPaymentStrategy.AskEachStatement,
            card.PaymentStrategy);
    }

    [Fact]
    public void AskEachWithoutFallback_RemainsUndetermined()
    {
        var statement = Assert.Single(_calculator.Project(
            Card() with { CarriedBalance = 94_000m },
            1,
            useProjectionFallback: true));

        Assert.Null(statement.Payment);
        Assert.Equal(
            CreditCardPaymentResolution.Undetermined,
            statement.PaymentResolution);
    }

    [Fact]
    public void AxessCanonicalStatements_AreExact()
    {
        var statements = _calculator.Project(
            TestFactory.AxessCard(),
            3,
            useProjectionFallback: true);

        AssertStatement(
            statements[0],
            new DateOnly(2026, 8, 25),
            new DateOnly(2026, 9, 5),
            96_485.68m,
            38_594.27m,
            57_891.41m,
            2_894.57m,
            60_785.98m);
        AssertStatement(
            statements[1],
            new DateOnly(2026, 9, 25),
            new DateOnly(2026, 10, 5),
            60_785.98m,
            24_314.39m,
            36_471.59m,
            1_823.58m,
            38_295.17m);
        AssertStatement(
            statements[2],
            new DateOnly(2026, 10, 25),
            new DateOnly(2026, 11, 5),
            53_833.53m,
            21_533.41m,
            32_300.12m,
            1_615.01m,
            33_915.13m);
    }

    [Fact]
    public void KnownDebt_IsSumOfDistinctOutstandingComponents()
    {
        Assert.Equal(
            123_751.49m,
            TestFactory.AxessCard().KnownTotalDebt);
    }

    private static void AssertStatement(
        CreditCardStatementProjection statement,
        DateOnly closeDate,
        DateOnly dueDate,
        decimal balance,
        decimal payment,
        decimal carriedPrincipal,
        decimal interest,
        decimal nextCarry)
    {
        Assert.Equal(closeDate, statement.StatementCloseDate);
        Assert.Equal(dueDate, statement.PaymentDueDate);
        Assert.Equal(balance, statement.StatementBalance);
        Assert.Equal(payment, statement.Payment);
        Assert.Equal(carriedPrincipal, statement.CarriedAfterPayment);
        Assert.Equal(interest, statement.CarryInterest);
        Assert.Equal(nextCarry, statement.NextCarriedBalance);
    }

    private static CreditCard Card() => new()
    {
        Name = "Test",
        BalanceAsOfDate = new DateOnly(2026, 8, 1),
        StatementClosingDay = 25,
        PaymentDueDay = 5,
        MinimumPaymentRate = 0.40m
    };

    private static CardCharge Charge(
        DateOnly postingDate,
        decimal amount) => new()
    {
        PostingDate = postingDate,
        Amount = amount
    };
}

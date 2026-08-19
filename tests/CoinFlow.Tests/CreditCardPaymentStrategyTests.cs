using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class CreditCardPaymentStrategyTests
{
    private readonly CreditCardProjectionCalculator _calculator = new();

    [Fact]
    public void NewCard_DefaultsToAskEachStatement()
    {
        Assert.Equal(
            CreditCardPaymentStrategy.AskEachStatement,
            new CreditCard().PaymentStrategy);
    }

    [Fact]
    public void AskEachStatementWithoutPlan_DoesNotCreateMinimumPayment()
    {
        var card = StatementCard();

        var statement = Assert.Single(_calculator.Project(card, 1));

        Assert.Null(statement.Payment);
        Assert.Equal(CreditCardPaymentResolution.Undetermined, statement.PaymentResolution);
        Assert.Empty(card.PaymentPlans);
    }

    [Fact]
    public void MinimumStrategy_PaysCalculatedMinimum()
    {
        var card = StatementCard() with { PaymentStrategy = CreditCardPaymentStrategy.Minimum };

        var statement = Assert.Single(_calculator.Project(card, 1));

        Assert.Equal(94_000m, statement.StatementBalance);
        Assert.Equal(37_600m, statement.MinimumPayment);
        Assert.Equal(37_600m, statement.Payment);
    }

    [Fact]
    public void FullStatementStrategy_PaysAllAndLeavesNoCarry()
    {
        var card = StatementCard() with { PaymentStrategy = CreditCardPaymentStrategy.FullStatement };

        var statement = Assert.Single(_calculator.Project(card, 1));

        Assert.Equal(94_000m, statement.Payment);
        Assert.Equal(0m, statement.CarriedAfterPayment);
    }

    [Fact]
    public void FixedFiftyThousand_PaysFiftyThousand()
    {
        var card = StatementCard() with
        {
            PaymentStrategy = CreditCardPaymentStrategy.FixedAmount,
            FixedPaymentAmount = 50_000m
        };

        Assert.Equal(50_000m, Assert.Single(_calculator.Project(card, 1)).Payment);
    }

    [Fact]
    public void FixedBelowMinimum_IsRaisedToMinimum()
    {
        var card = StatementCard() with
        {
            PaymentStrategy = CreditCardPaymentStrategy.FixedAmount,
            FixedPaymentAmount = 20_000m
        };

        Assert.Equal(37_600m, Assert.Single(_calculator.Project(card, 1)).Payment);
    }

    [Fact]
    public void DueDateOverride_TakesPriorityOverGeneralStrategy()
    {
        var card = StatementCard() with
        {
            PaymentStrategy = CreditCardPaymentStrategy.Minimum,
            PaymentPlans =
            [
                new CreditCardPaymentPlan
                {
                    DueDate = new DateOnly(2026, 10, 5),
                    PaymentType = CreditCardPaymentType.FixedAmount,
                    Amount = 60_000m
                }
            ]
        };

        var statement = Assert.Single(_calculator.Project(card, 1));

        Assert.Equal(60_000m, statement.Payment);
        Assert.Equal(CreditCardPaymentResolution.DueDateOverride, statement.PaymentResolution);
    }

    [Fact]
    public void DueDateOverride_DoesNotLeakIntoFollowingStatement()
    {
        var card = StatementCard() with
        {
            PaymentStrategy = CreditCardPaymentStrategy.Minimum,
            PaymentPlans =
            [
                new CreditCardPaymentPlan
                {
                    DueDate = new DateOnly(2026, 10, 5),
                    PaymentType = CreditCardPaymentType.FixedAmount,
                    Amount = 50_000m
                }
            ]
        };

        var statements = _calculator.Project(card, 2);

        Assert.Equal(50_000m, statements[0].Payment);
        Assert.Equal(CreditCardPaymentResolution.DueDateOverride, statements[0].PaymentResolution);
        Assert.Equal(17_600m, statements[1].Payment);
        Assert.Equal(CreditCardPaymentResolution.GeneralStrategy, statements[1].PaymentResolution);
    }

    [Fact]
    public void AskEachStatementWithMinimumFallback_UsesEstimateWithoutCreatingPlan()
    {
        var card = StatementCard() with
        {
            ProjectionFallbackStrategy = ProjectionFallbackStrategy.Minimum
        };

        var statement = Assert.Single(_calculator.Project(card, 1, useProjectionFallback: true));

        Assert.Equal(37_600m, statement.Payment);
        Assert.Equal(CreditCardPaymentResolution.ProjectionFallback, statement.PaymentResolution);
        Assert.Empty(card.PaymentPlans);
    }

    [Fact]
    public void AskEachStatementWithFullFallback_UsesFullEstimateWithoutCreatingPlan()
    {
        var card = StatementCard() with
        {
            ProjectionFallbackStrategy = ProjectionFallbackStrategy.FullStatement
        };

        var statement = Assert.Single(_calculator.Project(card, 1, useProjectionFallback: true));

        Assert.Equal(94_000m, statement.Payment);
        Assert.Equal(CreditCardPaymentResolution.ProjectionFallback, statement.PaymentResolution);
        Assert.Empty(card.PaymentPlans);
    }

    [Fact]
    public void LaterPaymentPlan_ReplacesProjectionFallbackForExactDueDate()
    {
        var card = StatementCard() with
        {
            ProjectionFallbackStrategy = ProjectionFallbackStrategy.Minimum,
            PaymentPlans =
            [
                new CreditCardPaymentPlan
                {
                    DueDate = new DateOnly(2026, 10, 5),
                    PaymentType = CreditCardPaymentType.FixedAmount,
                    Amount = 50_000m
                }
            ]
        };

        var statement = Assert.Single(_calculator.Project(card, 1, useProjectionFallback: true));

        Assert.Equal(50_000m, statement.Payment);
        Assert.Equal(CreditCardPaymentResolution.DueDateOverride, statement.PaymentResolution);
    }

    [Fact]
    public void DifferentCards_KeepIndependentStrategies()
    {
        var minimumCard = StatementCard() with
        {
            PaymentStrategy = CreditCardPaymentStrategy.Minimum
        };
        var fullCard = StatementCard() with
        {
            PaymentStrategy = CreditCardPaymentStrategy.FullStatement
        };

        Assert.Equal(37_600m, Assert.Single(_calculator.Project(minimumCard, 1)).Payment);
        Assert.Equal(94_000m, Assert.Single(_calculator.Project(fullCard, 1)).Payment);
    }

    [Fact]
    public void ProjectionFallback_IsEstimateAndNotConfirmedMandatoryPayment()
    {
        var calculator = new MandatoryPaymentCalculator(new LoanScheduleCalculator());
        var estimate = new ObligationItem(
            "Akbank Axess",
            ObligationType.CreditCard,
            new DateOnly(2026, 9, 5),
            37_600m,
            IsEstimate: true);

        var summary = calculator.Calculate(
            new SalaryPeriod(new DateOnly(2026, 8, 10), new DateOnly(2026, 9, 10)),
            [],
            [],
            [estimate],
            0m);

        Assert.Equal(0m, summary.Total);
        Assert.Equal(37_600m, summary.ProjectedTotal);
    }

    private static CreditCard StatementCard() => new()
    {
        Name = "Axess",
        Bank = "Akbank",
        CarriedBalance = 35_000m,
        UnbilledSpending = 59_000m,
        BalanceAsOfDate = new DateOnly(2026, 8, 26),
        StatementClosingDay = 25,
        PaymentDueDay = 5,
        MinimumPaymentRate = 0.40m
    };
}

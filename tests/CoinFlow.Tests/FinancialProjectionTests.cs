using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

public sealed class FinancialProjectionTests
{
    private readonly FinancialProjectionCalculator _calculator =
        TestFactory.ProjectionCalculator();

    [Fact]
    public void FirstFourCanonicalSalaryPeriods_AreExact()
    {
        var rows = _calculator.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            4);

        AssertRow(
            rows[0], 115_000m, 21_875.82m, 24_314.39m, 28_167.40m,
            74_357.61m, 40_642.39m, 30_000m, 10_642.39m);
        AssertRow(
            rows[1], 115_000m, 21_875.82m, 21_533.41m, 28_167.40m,
            71_576.63m, 43_423.37m, 30_000m, 13_423.37m);
        AssertRow(
            rows[2], 115_000m, 21_875.82m, 17_207.21m, 55_492.20m,
            94_575.23m, 20_424.77m, 30_000m, -9_575.23m);

        Assert.Equal(10_642.39m, rows[0].EndingProjectedSavings);
        Assert.Equal(24_065.76m, rows[1].EndingProjectedSavings);
        Assert.Equal(14_490.53m, rows[2].EndingProjectedSavings);
        Assert.Equal(new DateOnly(2026, 12, 10), rows[3].PeriodStart);
    }

    [Fact]
    public void CanonicalUpcomingPlan_ExposesPreSalaryObligationsSeparately()
    {
        var result = _calculator.CalculatePlan(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            12);

        Assert.Equal(2, result.FundingPlan.PreFirstSalaryObligations.Count);
        Assert.Equal(
            53_095.50m,
            result.FundingPlan.PreFirstSalaryObligations.Sum(x => x.Amount));
        Assert.Equal(
            result.FundingPlan.EligiblePaymentCount,
            result.FundingPlan.AssignedExactlyOnceCount);
        Assert.Equal(0, result.FundingPlan.UnassignedPaymentCount);
        Assert.Equal(0, result.FundingPlan.DuplicateAssignedCount);
    }

    [Fact]
    public void AvailableAfterMandatory_IsNotClamped()
    {
        var plan = BasicPlan(100m) with
        {
            Loans =
            [
                new Loan
                {
                    MonthlyPayment = 500m,
                    PaymentDay = 18,
                    NextPaymentDate = new DateOnly(2026, 9, 18),
                    RemainingInstallmentCount = 1
                }
            ]
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(-400m, row.AvailableAfterMandatory);
    }

    [Fact]
    public void LivingBudget_ProducesNegativeSavingsCapacity()
    {
        var plan = BasicPlan(25_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m
            }
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(25_000m, row.AvailableAfterMandatory);
        Assert.Equal(-5_000m, row.EstimatedSavingsCapacity);
        Assert.True(row.HasDeficit);
    }

    [Fact]
    public void ProjectedSavings_IsCumulativeFromConfiguredStart()
    {
        var plan = BasicPlan(50_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionStartingSavings = 100_000m
            }
        };

        var rows = _calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            3);

        Assert.Equal(100_000m, rows[0].OpeningProjectedSavings);
        Assert.Equal(120_000m, rows[0].EndingProjectedSavings);
        Assert.Equal(120_000m, rows[1].OpeningProjectedSavings);
        Assert.Equal(160_000m, rows[2].EndingProjectedSavings);
    }

    [Fact]
    public void CarryOverDeficit_IsVisibleWithoutBeingCountedTwice()
    {
        var paymentPlanId = Guid.NewGuid();
        var plan = BasicPlan(115_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionAnchorDate = new DateOnly(2026, 8, 20)
            },
            PaymentPlans =
            [
                new TemporaryPaymentPlan
                {
                    Id = paymentPlanId,
                    Name = "Exact ödemeler",
                    Kind = PaymentPlanKind.Temporary,
                    Installments =
                    [
                        new TemporaryPaymentInstallment
                        {
                            PlanId = paymentPlanId,
                            DueDate = new DateOnly(2026, 9, 20),
                            Amount = 110_987m
                        },
                        new TemporaryPaymentInstallment
                        {
                            PlanId = paymentPlanId,
                            DueDate = new DateOnly(2026, 10, 20),
                            Amount = 50_043m
                        }
                    ]
                }
            ]
        };

        var rows = _calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            2);
        var current = rows[1];

        Assert.Equal(-25_987m,
            rows[0].EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(1_299.35m, rows[0].DeficitFinancingInterest);
        Assert.Equal(-27_286.35m, rows[0].EndingProjectedSavings);
        Assert.Equal(-27_286.35m, current.OpeningProjectedSavings);
        Assert.Equal(115_000m, current.TotalIncome);
        Assert.Equal(50_043m, current.MandatoryOutflow);
        Assert.Equal(64_957m, current.AvailableAfterMandatory);
        Assert.Equal(27_286.35m, current.CarryOverDeficit);
        Assert.Equal(37_670.65m,
            current.AvailableAfterCarryOverDeficit);
        Assert.Equal(30_000m, current.LivingBudget);
        Assert.Equal(34_957m, current.EstimatedSavingsCapacity);
        Assert.Equal(7_670.65m,
            current.EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(0m, current.DeficitFinancingInterest);
        Assert.Equal(7_670.65m, current.EndingProjectedSavings);
        Assert.True(current.RecoveredCarryOverDeficit);
        Assert.Single(current.MandatoryItems);
        Assert.Equal(50_043m, current.MandatoryItems[0].Amount);
        Assert.Empty(plan.CreditCards);
        Assert.Empty(plan.Loans);
    }

    [Fact]
    public void DeficitContinuesIntoNextOpeningWithoutClampOrFakeObligation()
    {
        var plan = BasicPlan(50_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionStartingSavings = -25_000m,
                ProjectionAnchorDate = new DateOnly(2026, 8, 20)
            }
        };

        var rows = _calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            2);

        Assert.Equal(25_000m, rows[0].CarryOverDeficit);
        Assert.Equal(20_000m, rows[0].CurrentPeriodNetContribution);
        Assert.Equal(-5_000m,
            rows[0].EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(250m, rows[0].DeficitFinancingInterest);
        Assert.Equal(-5_250m, rows[0].EndingProjectedSavings);
        Assert.Equal(-5_250m, rows[1].OpeningProjectedSavings);
        Assert.Equal(5_250m, rows[1].CarryOverDeficit);
        Assert.Empty(rows[0].MandatoryItems);
        Assert.Empty(rows[1].MandatoryItems);
    }

    [Fact]
    public void DeficitRecovery_ProducesPositiveEndingWithoutDoubleCount()
    {
        var plan = BasicPlan(70_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionStartingSavings = -25_000m,
                ProjectionAnchorDate = new DateOnly(2026, 8, 20)
            }
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(25_000m, row.CarryOverDeficit);
        Assert.Equal(40_000m, row.CurrentPeriodNetContribution);
        Assert.Equal(15_000m, row.EndingProjectedSavings);
        Assert.True(row.RecoveredCarryOverDeficit);
    }

    [Fact]
    public void PositiveOpeningSavings_DoesNotCreateCarryOverDeficit()
    {
        var plan = BasicPlan(50_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionStartingSavings = 10_000m,
                ProjectionAnchorDate = new DateOnly(2026, 8, 20)
            }
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(0m, row.CarryOverDeficit);
        Assert.False(row.HasCarryOverDeficit);
        Assert.Equal(20_000m, row.CurrentPeriodNetContribution);
        Assert.Equal(30_000m, row.EndingProjectedSavings);
    }

    [Fact]
    public void LargeCashExpense_ImpactsItsPeriodAndAllFutureSavings()
    {
        var baseline = BasicPlan(50_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m
            }
        };
        var scenario = baseline with
        {
            PlannedLargeExpenses =
            [
                new PlannedLargeExpense
                {
                    Name = "Tadilat",
                    Amount = 350_000m,
                    ExactDate = new DateOnly(2026, 9, 15)
                }
            ]
        };

        var baselineRows = _calculator.Calculate(
            baseline, new DateOnly(2026, 8, 20), 3);
        var scenarioRows = _calculator.Calculate(
            scenario, new DateOnly(2026, 8, 20), 3);

        Assert.Equal(350_000m, scenarioRows[0].PlannedLargeCashExpenses);
        Assert.Equal(
            baselineRows[0].EstimatedSavingsCapacity - 350_000m,
            scenarioRows[0].EstimatedSavingsCapacity);
        Assert.Equal(-330_000m,
            scenarioRows[0].EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(16_500m,
            scenarioRows[0].DeficitFinancingInterest);
        Assert.Equal(-346_500m,
            scenarioRows[0].EndingProjectedSavings);
        Assert.Equal(-338_966.25m,
            scenarioRows[2].EndingProjectedSavings);
    }

    [Fact]
    public void JanuarySalaryIncrease_StartsWithJanuarySalaryPeriod()
    {
        var rows = _calculator.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 12, 10),
            5);

        var december = rows.Single(x =>
            x.PeriodStart == new DateOnly(2026, 12, 10));
        var january = rows.Single(x =>
            x.PeriodStart == new DateOnly(2027, 1, 10));
        Assert.Equal(115_000m, december.SalaryIncome);
        Assert.Equal(132_250m, january.SalaryIncome);
    }

    [Fact]
    public void OtherIncome_IsAddedOnlyToItsExactSalaryPeriod()
    {
        var plan = BasicPlan(100_000m) with
        {
            OtherIncomes =
            [
                new OneTimeIncome
                {
                    Amount = 50_000m,
                    ExactDate = new DateOnly(2026, 9, 15)
                }
            ]
        };

        var rows = _calculator.Calculate(
            plan, new DateOnly(2026, 8, 20), 3);

        Assert.Equal(50_000m, rows[0].OtherIncome);
        Assert.Equal(0m, rows[1].OtherIncome);
        Assert.Equal(0m, rows[2].OtherIncome);
    }

    [Fact]
    public void CardFallback_IsIncludedAndMarkedEstimated()
    {
        var row = Assert.Single(_calculator.Calculate(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(24_314.39m, row.CreditCardPayments);
        Assert.True(row.IsEstimatedCardPayment);
        Assert.False(row.HasUndeterminedCardPayment);
    }

    [Fact]
    public void PreviousFullStatement_AssignsSeptemberStatementToSeptemberSalary()
    {
        var canonical = TestFactory.CanonicalPlan();
        var plan = canonical with
        {
            Loans = [canonical.Loans[0]],
            PaymentPlans = [],
            CreditCards =
            [
                TestFactory.AxessCard() with
                {
                    PaymentStrategy = CreditCardPaymentStrategy.FullStatement
                }
            ],
            PaymentAssignmentStrategies =
            [
                new PaymentAssignmentStrategy
                {
                    Mode = PaymentAssignmentMode.PreviousPeriod,
                    EffectiveFromSalaryDate = new DateOnly(2026, 9, 10)
                }
            ]
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(new DateOnly(2026, 9, 10), row.PeriodStart);
        Assert.Equal(115_000m, row.TotalIncome);
        Assert.Equal(14_501.23m, row.LoanPayments);
        Assert.Equal(96_485.68m, row.CreditCardPayments);
        Assert.Equal(110_986.91m, row.MandatoryOutflow);
        Assert.Equal(-25_986.91m, row.EstimatedSavingsCapacity);
        Assert.Equal(1_299.35m, row.DeficitFinancingInterest);
        Assert.Equal(-27_286.26m, row.EndingProjectedSavings);
    }

    [Fact]
    public void DeficitInterest_CompoundsAndStopsOnRecovery()
    {
        var continuing = BasicPlan(40_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionStartingSavings = -26_250m,
                ProjectionAnchorDate = new DateOnly(2026, 8, 20),
                DeficitFinancingInterestRate = 0.05m
            }
        };
        var row = Assert.Single(_calculator.Calculate(
            continuing,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(-16_250m,
            row.EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(16_250m, row.DeficitPrincipal);
        Assert.Equal(812.50m, row.DeficitFinancingInterest);
        Assert.Equal(-17_062.50m, row.EndingProjectedSavings);

        var recovered = continuing with
        {
            Salaries =
            [
                new SalaryScheduleEntry
                {
                    Amount = 70_000m,
                    EffectiveDate = new DateOnly(2026, 1, 1)
                }
            ]
        };
        var recovery = Assert.Single(_calculator.Calculate(
            recovered,
            new DateOnly(2026, 8, 20),
            1));
        Assert.Equal(13_750m,
            recovery.EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(0m, recovery.DeficitFinancingInterest);
        Assert.Equal(13_750m, recovery.EndingProjectedSavings);
    }

    [Fact]
    public void ZeroDeficitInterestRate_LeavesNegativeEndingUnchanged()
    {
        var plan = BasicPlan(5_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionAnchorDate = new DateOnly(2026, 8, 20),
                DeficitFinancingInterestRate = 0m
            }
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(-25_000m,
            row.EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(0m, row.DeficitFinancingInterest);
        Assert.Equal(-25_000m, row.EndingProjectedSavings);
    }

    [Fact]
    public void CardAndDeficitInterest_RemainSeparateAndTotalExactly()
    {
        var card = new CreditCard
        {
            Name = "Test kart",
            CarriedBalance = 100_000m,
            BalanceAsOfDate = new DateOnly(2026, 8, 26),
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentStrategy = CreditCardPaymentStrategy.Minimum
        };
        var plan = BasicPlan(45_000m) with
        {
            Settings = new UserSettings
            {
                SalaryDay = 10,
                MonthlyLivingBudget = 30_000m,
                ProjectionAnchorDate = new DateOnly(2026, 8, 20),
                CreditCardCarryInterestRate = 0.05m,
                DeficitFinancingInterestRate = 0.05m
            },
            Salaries =
            [
                new SalaryScheduleEntry
                {
                    Amount = 45_000m,
                    EffectiveDate = new DateOnly(2026, 1, 1)
                },
                new SalaryScheduleEntry
                {
                    Amount = 65_200m,
                    EffectiveDate = new DateOnly(2026, 10, 10)
                }
            ],
            CreditCards = [card]
        };

        var result = _calculator.CalculatePlan(
            plan,
            new DateOnly(2026, 8, 20),
            2);

        Assert.Equal(40_000m, result.Periods[0].MandatoryOutflow);
        Assert.Equal(-25_000m,
            result.Periods[0].EstimatedSavingsCapacity);
        Assert.Equal(-25_000m,
            result.Periods[0].EndingProjectedSavingsBeforeDeficitInterest);
        Assert.Equal(3_000m, result.Periods[0].CardInterestGenerated);
        Assert.Equal(1_250m, result.Periods[0].DeficitFinancingInterest);
        Assert.Equal(4_250m, result.Periods[0].TotalInterestGenerated);
        Assert.Equal(1_890m, result.Periods[1].CardInterestGenerated);
        Assert.Equal(812.50m,
            result.Periods[1].DeficitFinancingInterest);
        Assert.Equal(2_702.50m,
            result.Periods[1].TotalInterestGenerated);
        Assert.Equal(4_890m, result.TotalCreditCardInterest);
        Assert.Equal(2_062.50m,
            result.TotalDeficitFinancingInterest);
        Assert.Equal(6_952.50m, result.TotalInterestCost);
        Assert.Empty(plan.PaymentPlans);
        Assert.Empty(plan.Loans);
        Assert.Empty(plan.PlannedLargeExpenses);
    }

    [Fact]
    public void CanonicalTwelvePeriodInterestTotals_AreExact()
    {
        var result = _calculator.CalculatePlan(
            TestFactory.CanonicalPlan(),
            new DateOnly(2026, 8, 20),
            12);

        Assert.Equal(7_101.67m, result.TotalCreditCardInterest);
        Assert.Equal(0m, result.TotalDeficitFinancingInterest);
        Assert.Equal(7_101.67m, result.TotalInterestCost);
    }

    [Fact]
    public void AskEachWithoutFallback_IsVisibleAsUndetermined()
    {
        var plan = BasicPlan(100_000m) with
        {
            CreditCards =
            [
                TestFactory.AxessCard() with
                {
                    ProjectionFallbackStrategy =
                        ProjectionFallbackStrategy.None
                }
            ]
        };

        var row = Assert.Single(_calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            1));

        Assert.Equal(0m, row.CreditCardPayments);
        Assert.True(row.HasUndeterminedCardPayment);
    }

    private static FinancialPlan BasicPlan(decimal salary) => new()
    {
        Settings = new UserSettings
        {
            SalaryDay = 10,
            ProjectionAnchorDate = new DateOnly(2026, 8, 20)
        },
        Salaries =
        [
            new SalaryScheduleEntry
            {
                Amount = salary,
                EffectiveDate = new DateOnly(2026, 1, 1)
            }
        ],
        PaymentAssignmentStrategies =
        [
            new PaymentAssignmentStrategy
            {
                Mode = PaymentAssignmentMode.UpcomingPeriod,
                EffectiveFromSalaryDate = new DateOnly(2026, 9, 10)
            }
        ]
    };

    private static void AssertRow(
        SalaryPeriodProjection row,
        decimal income,
        decimal loans,
        decimal card,
        decimal temporary,
        decimal mandatory,
        decimal available,
        decimal living,
        decimal savings)
    {
        Assert.Equal(income, row.TotalIncome);
        Assert.Equal(loans, row.LoanPayments);
        Assert.Equal(card, row.CreditCardPayments);
        Assert.Equal(temporary, row.TemporaryPayments);
        Assert.Equal(mandatory, row.MandatoryOutflow);
        Assert.Equal(available, row.AvailableAfterMandatory);
        Assert.Equal(living, row.LivingBudget);
        Assert.Equal(savings, row.EstimatedSavingsCapacity);
    }
}

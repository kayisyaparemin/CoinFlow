using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Application.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

public sealed class SimulationTests
{
    private readonly FinancialProjectionCalculator _projection =
        TestFactory.ProjectionCalculator();
    private readonly InstallmentScheduleCalculator _installments = new();

    [Fact]
    public void InterestFree120000OverNinePayments_IsExactAndBaselineUnchanged()
    {
        var plan = TestFactory.CanonicalPlan();
        var calculator = new SimulationCalculator(
            _projection,
            _installments);
        var request = new SimulationRequest(
            SimulationScenarioType.CashDebt,
            "Beyaz eşya",
            120_000m,
            new DateOnly(2026, 12, 1),
            9,
            new DateOnly(2026, 12, 20));
        var baselineBefore = _projection.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            12);

        var scenarioPlan = calculator.BuildScenarioPlan(plan, request);
        var result = calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            request);
        var addedPlan = scenarioPlan.PaymentPlans
            .Single(x => plan.PaymentPlans.All(p => p.Id != x.Id));

        Assert.Equal(9, addedPlan.Installments.Count);
        Assert.Equal(
            120_000m,
            addedPlan.Installments.Sum(x => x.Amount));
        Assert.Equal(
            new DateOnly(2026, 12, 20),
            addedPlan.Installments[0].DueDate);
        Assert.Equal(
            new DateOnly(2027, 8, 20),
            addedPlan.Installments[^1].DueDate);
        Assert.Equal(
            baselineBefore.Select(x => x.EndingProjectedSavings),
            result.Baseline.Select(x => x.EndingProjectedSavings));
        Assert.Single(plan.PaymentPlans);
    }

    [Fact]
    public void CashRenovation_ReducesCumulativeSavingsFromExactPeriod()
    {
        var plan = TestFactory.CanonicalPlan();
        var result = new SimulationCalculator(_projection, _installments)
            .Calculate(
                plan,
                new DateOnly(2026, 8, 20),
                new SimulationRequest(
                    SimulationScenarioType.CashPurchase,
                    "Tadilat",
                    350_000m,
                    new DateOnly(2027, 3, 15)));

        var impacted = result.Rows.Single(x =>
            x.Scenario.Period.Contains(new DateOnly(2027, 3, 15)));
        Assert.Equal(
            -350_000m,
            impacted.ProjectedSavingsDifference);
        Assert.All(
            result.Rows.Where(x =>
                x.Scenario.PeriodStart > impacted.Scenario.PeriodStart),
            row => Assert.Equal(-350_000m, row.ProjectedSavingsDifference));
    }

    [Fact]
    public void CardInstallmentScenario_UsesSharedStatementEngine()
    {
        var plan = TestFactory.CanonicalPlan();
        var card = Assert.Single(plan.CreditCards);
        var request = new SimulationRequest(
            SimulationScenarioType.CreditCardInstallmentPurchase,
            "Beyaz eşya",
            120_000m,
            new DateOnly(2026, 9, 24),
            9,
            CreditCardId: card.Id);
        var calculator = new SimulationCalculator(
            _projection,
            _installments);

        var scenarioPlan = calculator.BuildScenarioPlan(plan, request);
        var scenarioCard = Assert.Single(scenarioPlan.CreditCards);
        var statements = new CreditCardStatementCalculator().Project(
            scenarioCard,
            4,
            useProjectionFallback: true);
        var result = calculator.Calculate(
            plan,
            new DateOnly(2026, 8, 20),
            request);

        Assert.Equal(
            card.Charges.Count + 9,
            scenarioCard.Charges.Count);
        Assert.Contains(
            statements,
            x => x.StatementCloseDate == new DateOnly(2026, 9, 25) &&
                 x.NewCharges > 0m);
        Assert.Contains(result.Rows, x =>
            x.Scenario.CreditCardPayments !=
            x.Baseline.CreditCardPayments);
    }

    [Fact]
    public void Financing_ReportsTotalAndFinancingCost()
    {
        var result = new SimulationCalculator(_projection, _installments)
            .Calculate(
                TestFactory.CanonicalPlan(),
                new DateOnly(2026, 8, 20),
                new SimulationRequest(
                    SimulationScenarioType.FinancingLoan,
                    "Finansman",
                    120_000m,
                    new DateOnly(2026, 12, 1),
                    9,
                    new DateOnly(2026, 12, 20),
                    TotalRepaymentAmount: 145_000m));

        Assert.Equal(145_000m, result.Risk.TotalScenarioCost);
        Assert.Equal(25_000m, result.Risk.FinancingCost);
    }

    [Fact]
    public void FutureIncome_IncreasesOnlyScenarioProjection()
    {
        var result = new SimulationCalculator(_projection, _installments)
            .Calculate(
                TestFactory.CanonicalPlan(),
                new DateOnly(2026, 8, 20),
                new SimulationRequest(
                    SimulationScenarioType.FutureIncome,
                    "Bonus",
                    100_000m,
                    new DateOnly(2027, 3, 15)));

        var row = result.Rows.Single(x =>
            x.Scenario.Period.Contains(new DateOnly(2027, 3, 15)));
        Assert.Equal(100_000m, row.Scenario.OtherIncome);
        Assert.Equal(0m, row.Baseline.OtherIncome);
    }

    [Fact]
    public void ScenarioDeficit_CarriesForwardAndReportsRecovery()
    {
        var plan = CarryOverPlan();
        var result = new SimulationCalculator(_projection, _installments)
            .Calculate(
                plan,
                new DateOnly(2026, 8, 20),
                new SimulationRequest(
                    SimulationScenarioType.CashPurchase,
                    "Planlı nakit gider",
                    45_000m,
                    new DateOnly(2026, 9, 20)),
                periodCount: 4);

        Assert.Equal(-25_000m, result.Scenario[0].EndingProjectedSavings);
        Assert.Equal(-25_000m, result.Scenario[1].OpeningProjectedSavings);
        Assert.Equal(25_000m, result.Scenario[1].CarryOverDeficit);
        Assert.Equal(-5_000m, result.Scenario[1].EndingProjectedSavings);
        Assert.Equal(-5_000m, result.Scenario[2].OpeningProjectedSavings);
        Assert.Equal(15_000m, result.Scenario[2].EndingProjectedSavings);
        Assert.Equal(new DateOnly(2026, 9, 10),
            result.Risk.FirstDeficitPeriod?.Start);
        Assert.Equal(25_000m, result.Risk.MaximumCarryOverDeficit);
        Assert.Equal(new DateOnly(2026, 11, 10),
            result.Risk.RecoveryPeriod?.Start);

        var scenarioPlan = new SimulationCalculator(
            _projection,
            _installments).BuildScenarioPlan(
                plan,
                new SimulationRequest(
                    SimulationScenarioType.CashPurchase,
                    "Planlı nakit gider",
                    45_000m,
                    new DateOnly(2026, 9, 20)));
        Assert.Empty(scenarioPlan.PaymentPlans);
        Assert.Empty(scenarioPlan.CreditCards);
        Assert.Single(scenarioPlan.PlannedLargeExpenses);
    }

    [Fact]
    public void SalaryChange_UsesPeriodStartEffectiveRule()
    {
        var result = new SimulationCalculator(_projection, _installments)
            .Calculate(
                TestFactory.CanonicalPlan(),
                new DateOnly(2026, 8, 20),
                new SimulationRequest(
                    SimulationScenarioType.SalaryChange,
                    "Yeni maaş",
                    150_000m,
                    new DateOnly(2027, 1, 1)));

        Assert.Equal(
            115_000m,
            result.Scenario.Single(x =>
                x.PeriodStart == new DateOnly(2026, 12, 10))
                .SalaryIncome);
        Assert.Equal(
            150_000m,
            result.Scenario.Single(x =>
                x.PeriodStart == new DateOnly(2027, 1, 10))
                .SalaryIncome);
    }

    [Fact]
    public async Task Simulate_DoesNotMutateSqlite()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store);
            var before = await service.GetFinancialPlanAsync();
            await service.SimulateAsync(new SimulationRequest(
                SimulationScenarioType.CashDebt,
                "Beyaz eşya",
                120_000m,
                new DateOnly(2026, 12, 1),
                9,
                new DateOnly(2026, 12, 20)));
            var after = await service.GetFinancialPlanAsync();

            Assert.Equal(before.PaymentPlans.Count, after.PaymentPlans.Count);
            Assert.Equal(
                before.PlannedLargeExpenses.Count,
                after.PlannedLargeExpenses.Count);
            Assert.Equal(
                before.CreditCards.Single().Charges.Count,
                after.CreditCards.Single().Charges.Count);
        });
    }

    [Fact]
    public async Task ApplyPlan_RequiresConfirmationThenPersists()
    {
        await WithStore(async store =>
        {
            var service = TestFactory.Service(store);
            var request = new SimulationRequest(
                SimulationScenarioType.CashPurchase,
                "Tadilat",
                350_000m,
                new DateOnly(2027, 3, 15),
                ScenarioId: Guid.NewGuid());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApplySimulationAsync(request, confirmed: false));
            Assert.Empty(
                (await service.GetFinancialPlanAsync())
                .PlannedLargeExpenses);

            await service.ApplySimulationAsync(request, confirmed: true);
            var applied = Assert.Single(
                (await service.GetFinancialPlanAsync())
                .PlannedLargeExpenses);
            Assert.Equal(350_000m, applied.Amount);
        }, seed: false);
    }

    [Fact]
    public async Task ApplyCanonicalScenarios_PersistAndSurviveRestart()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-apply-restart-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = new(
            path,
            developmentFeaturesEnabled: false,
            new DateOnly(2026, 8, 20));
        try
        {
            await PrepareCanonicalPlanAsync(store);
            var service = TestFactory.Service(store);

            var cashId = Guid.NewGuid();
            var cashResult = await service.ApplySimulationAsync(
                new SimulationRequest(
                    SimulationScenarioType.CashPurchase,
                    "Tadilat",
                    350_000m,
                    new DateOnly(2027, 3, 15),
                    ScenarioId: cashId),
                confirmed: true);
            Assert.Equal(SimulationApplyDestination.Payments, cashResult.Destination);
            Assert.Equal(cashId, Assert.Single(
                (await service.GetFinancialPlanAsync()).PlannedLargeExpenses).Id);

            var beforeFinancing = await service.GetFuturePeriodsAsync();
            var financingId = Guid.NewGuid();
            var financingResult = await service.ApplySimulationAsync(
                new SimulationRequest(
                    SimulationScenarioType.FinancingLoan,
                    "Beyaz eşya finansmanı",
                    120_000m,
                    new DateOnly(2026, 12, 1),
                    9,
                    new DateOnly(2026, 12, 20),
                    TotalRepaymentAmount: 145_000m,
                    ScenarioId: financingId),
                confirmed: true);
            Assert.Equal(
                SimulationApplyDestination.Payments,
                financingResult.Destination);
            var financing = (await service.GetFinancialPlanAsync())
                .PaymentPlans.Single(x => x.Id == financingId);
            Assert.Equal(PaymentPlanKind.Installment, financing.Kind);
            Assert.Equal(120_000m, financing.OriginalAmount);
            Assert.Equal(145_000m, financing.TotalRepaymentAmount);
            Assert.Equal(9, financing.Installments.Count);
            Assert.Equal(145_000m, financing.Installments.Sum(x => x.Amount));
            Assert.Equal(new DateOnly(2026, 12, 20), financing.Installments[0].DueDate);
            Assert.Equal(new DateOnly(2027, 8, 20), financing.Installments[^1].DueDate);
            var afterFinancing = await service.GetFuturePeriodsAsync();
            Assert.True(afterFinancing.Single(x =>
                    x.Period.Contains(new DateOnly(2026, 12, 20)))
                .InstallmentPayments > beforeFinancing.Single(x =>
                    x.Period.Contains(new DateOnly(2026, 12, 20)))
                .InstallmentPayments);

            var card = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            var cardCountBefore = card.Charges.Count;
            var cardProjectionBefore = await service.GetFuturePeriodsAsync();
            var cardScenarioId = Guid.NewGuid();
            var cardRequest = new SimulationRequest(
                SimulationScenarioType.CreditCardInstallmentPurchase,
                "Beyaz eşya",
                120_000m,
                new DateOnly(2026, 12, 20),
                9,
                CreditCardId: card.Id,
                ScenarioId: cardScenarioId);
            var cardResult = await service.ApplySimulationAsync(
                cardRequest,
                confirmed: true);
            Assert.Equal(SimulationApplyDestination.CreditCard, cardResult.Destination);
            var cardAfter = Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards);
            var appliedCharges = cardAfter.Charges
                .Where(x => x.Description.StartsWith("Beyaz eşya", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(card.Id, cardAfter.Id);
            Assert.Equal(cardCountBefore + 9, cardAfter.Charges.Count);
            Assert.Equal(9, appliedCharges.Length);
            Assert.Equal(120_000m, appliedCharges.Sum(x => x.Amount));
            Assert.Contains(appliedCharges, x => x.Id == cardScenarioId);
            Assert.DoesNotContain(
                (await service.GetFinancialPlanAsync()).PaymentPlans,
                x => x.Name == "Beyaz eşya");
            var cardProjectionAfter = await service.GetFuturePeriodsAsync();
            Assert.False(cardProjectionBefore.Select(x => x.CreditCardPayments)
                .SequenceEqual(cardProjectionAfter.Select(x => x.CreditCardPayments)));

            var duplicate = await service.ApplySimulationAsync(
                cardRequest,
                confirmed: true);
            Assert.True(duplicate.AlreadyApplied);
            Assert.Equal(cardCountBefore + 9, Assert.Single(
                (await service.GetFinancialPlanAsync()).CreditCards).Charges.Count);

            var incomeId = Guid.NewGuid();
            var incomeResult = await service.ApplySimulationAsync(
                new SimulationRequest(
                    SimulationScenarioType.FutureIncome,
                    "Bonus",
                    100_000m,
                    new DateOnly(2027, 3, 15),
                    ScenarioId: incomeId),
                confirmed: true);
            Assert.Equal(
                SimulationApplyDestination.Income,
                incomeResult.Destination);
            var income = Assert.Single(
                (await service.GetFinancialPlanAsync()).OtherIncomes);
            Assert.Equal(incomeId, income.Id);
            Assert.Equal(100_000m, income.Amount);
            Assert.Equal(100_000m, (await service.GetFuturePeriodsAsync())
                .Single(x => x.Period.Contains(income.ExactDate)).OtherIncome);

            var salaryId = Guid.NewGuid();
            var salaryResult = await service.ApplySimulationAsync(
                new SimulationRequest(
                    SimulationScenarioType.SalaryChange,
                    "2027 maaşı",
                    132_250m,
                    new DateOnly(2027, 1, 1),
                    ScenarioId: salaryId),
                confirmed: true);
            Assert.Equal(
                SimulationApplyDestination.SalaryHistory,
                salaryResult.Destination);
            var salaries = (await service.GetFinancialPlanAsync()).Salaries;
            Assert.Contains(salaries, x =>
                x.Amount == 115_000m &&
                x.EffectiveDate == new DateOnly(2026, 1, 1));
            Assert.Contains(salaries, x =>
                x.Id == salaryId &&
                x.Amount == 132_250m &&
                x.EffectiveDate == new DateOnly(2027, 1, 1));

            var strategyId = Guid.NewGuid();
            var strategyResult = await service.ApplySimulationAsync(
                new SimulationRequest(
                    SimulationScenarioType.PaymentStrategyChange,
                    "Geçmiş dönemi kapat",
                    0m,
                    new DateOnly(2026, 12, 10),
                    NewPaymentAssignmentMode: PaymentAssignmentMode.PreviousPeriod,
                    EffectiveSalaryDate: new DateOnly(2026, 12, 10),
                    ScenarioId: strategyId),
                confirmed: true);
            Assert.Equal(
                SimulationApplyDestination.Settings,
                strategyResult.Destination);
            var strategies = (await service.GetFinancialPlanAsync())
                .PaymentAssignmentStrategies;
            Assert.Equal(2, strategies.Count);
            Assert.Contains(strategies, x =>
                x.EffectiveFromSalaryDate == new DateOnly(2026, 9, 10) &&
                x.Mode == PaymentAssignmentMode.UpcomingPeriod);
            Assert.Contains(strategies, x =>
                x.Id == strategyId &&
                x.EffectiveFromSalaryDate == new DateOnly(2026, 12, 10) &&
                x.Mode == PaymentAssignmentMode.PreviousPeriod);

            var refreshedSimulation = await service.SimulateAsync(
                new SimulationRequest(
                    SimulationScenarioType.FutureOneTimePayment,
                    "Yeni deneme",
                    1_000m,
                    new DateOnly(2027, 4, 15)));
            Assert.Equal(
                350_000m,
                refreshedSimulation.Baseline.Sum(x =>
                    x.PlannedLargeCashExpenses));
            Assert.Equal(
                100_000m,
                refreshedSimulation.Baseline.Sum(x => x.OtherIncome));

            await store.DisposeAsync();
            store = new SqliteCoinFlowStore(
                path,
                developmentFeaturesEnabled: false,
                new DateOnly(2026, 8, 20));
            var restarted = TestFactory.Service(store);
            var restartedPlan = await restarted.GetFinancialPlanAsync();
            Assert.Contains(restartedPlan.PlannedLargeExpenses, x => x.Id == cashId);
            Assert.Contains(restartedPlan.PaymentPlans, x =>
                x.Id == financingId && x.Installments.Count == 9);
            Assert.Contains(restartedPlan.CreditCards.Single().Charges, x =>
                x.Id == cardScenarioId);
            Assert.Contains(restartedPlan.OtherIncomes, x => x.Id == incomeId);
            Assert.Contains(restartedPlan.Salaries, x => x.Id == salaryId);
            Assert.Contains(restartedPlan.PaymentAssignmentStrategies, x =>
                x.Id == strategyId);
            Assert.Equal(
                350_000m,
                (await restarted.GetFuturePeriodsAsync()).Sum(x =>
                    x.PlannedLargeCashExpenses));
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync();
            }

            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task AggregateSaveFailure_RollsBackPaymentPlanChildren()
    {
        await WithStore(async store =>
        {
            var planId = Guid.NewGuid();
            var originalChild = Guid.NewGuid();
            await store.UpsertPaymentPlanAsync(new TemporaryPaymentPlan
            {
                Id = planId,
                Name = "Atomic plan",
                Kind = PaymentPlanKind.Installment,
                Installments =
                [
                    new TemporaryPaymentInstallment
                    {
                        Id = originalChild,
                        PlanId = planId,
                        DueDate = new DateOnly(2026, 12, 20),
                        Amount = 10_000m
                    }
                ]
            });

            var duplicateChild = Guid.NewGuid();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                store.UpsertPaymentPlanAsync(new TemporaryPaymentPlan
                {
                    Id = planId,
                    Name = "Broken replacement",
                    Kind = PaymentPlanKind.Installment,
                    Installments =
                    [
                        new TemporaryPaymentInstallment
                        {
                            Id = duplicateChild,
                            PlanId = planId,
                            DueDate = new DateOnly(2027, 1, 20),
                            Amount = 5_000m
                        },
                        new TemporaryPaymentInstallment
                        {
                            Id = duplicateChild,
                            PlanId = planId,
                            DueDate = new DateOnly(2027, 2, 20),
                            Amount = 5_000m
                        }
                    ]
                }));

            var persisted = Assert.Single(await store.GetPaymentPlansAsync());
            Assert.Equal("Atomic plan", persisted.Name);
            Assert.Equal(originalChild, Assert.Single(persisted.Installments).Id);
        }, seed: false);
    }

    [Theory]
    [InlineData(SimulationScenarioType.CashPurchase, 0, 1)]
    [InlineData(SimulationScenarioType.CreditCardInstallmentPurchase, 1000, 0)]
    [InlineData(SimulationScenarioType.FinancingLoan, 1000, 9)]
    public void InvalidApplyInput_IsRejectedBeforePersistence(
        SimulationScenarioType type,
        decimal amount,
        int paymentCount)
    {
        var request = new SimulationRequest(
            type,
            "Geçersiz",
            amount,
            new DateOnly(2026, 12, 1),
            paymentCount,
            CreditCardId: type == SimulationScenarioType.CreditCardInstallmentPurchase
                ? Guid.NewGuid()
                : null,
            TotalRepaymentAmount: type == SimulationScenarioType.FinancingLoan
                ? 1_200m
                : null,
            ScenarioId: Guid.NewGuid());

        Assert.ThrowsAny<Exception>(() => SimulationCalculator.Validate(request));
    }

    private static async Task PrepareCanonicalPlanAsync(
        SqliteCoinFlowStore store)
    {
        await store.InitializeAsync();
        await store.SaveSettingsAsync(new UserSettings
        {
            SalaryDay = 10,
            MonthlyLivingBudget = 30_000m,
            ProjectionStartingSavings = 0m,
            ProjectionAnchorDate = new DateOnly(2026, 8, 20)
        });
        await store.UpsertSalaryAsync(new SalaryScheduleEntry
        {
            Amount = 115_000m,
            EffectiveDate = new DateOnly(2026, 1, 1),
            Description = "Maaş"
        });
        await store.UpsertPaymentAssignmentStrategyAsync(
            new PaymentAssignmentStrategy
            {
                Mode = PaymentAssignmentMode.UpcomingPeriod,
                EffectiveFromSalaryDate = new DateOnly(2026, 9, 10),
                Note = "İlk düzen"
            });
        var cardId = Guid.NewGuid();
        await store.UpsertCreditCardAsync(new CreditCard
        {
            Id = cardId,
            Bank = "Akbank",
            Name = "Axess",
            Limit = 500_000m,
            BalanceAsOfDate = new DateOnly(2026, 8, 20),
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentStrategy = CreditCardPaymentStrategy.FullStatement,
            ProjectionFallbackStrategy = ProjectionFallbackStrategy.FullStatement
        });
    }

    private static async Task WithStore(
        Func<SqliteCoinFlowStore, Task> test,
        bool seed = true)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coinflow-simulation-{Guid.NewGuid():N}.db");
        var store = new SqliteCoinFlowStore(
            path,
            seed,
            new DateOnly(2026, 8, 20));
        try
        {
            if (seed)
            {
                await TestFactory.Service(store)
                    .LoadCanonicalDevelopmentDataAsync();
            }

            await test(store);
        }
        finally
        {
            await store.DisposeAsync();
            DeleteDatabase(path);
        }
    }

    private static FinancialPlan CarryOverPlan() => new()
    {
        Settings = new UserSettings
        {
            SalaryDay = 10,
            MonthlyLivingBudget = 30_000m,
            ProjectionAnchorDate = new DateOnly(2026, 8, 20)
        },
        Salaries =
        [
            new SalaryScheduleEntry
            {
                Amount = 50_000m,
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

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[]
                 {
                     path,
                     path + "-shm",
                     path + "-wal"
                 })
        {
            if (File.Exists(candidate))
            {
                File.Delete(candidate);
            }
        }
    }
}

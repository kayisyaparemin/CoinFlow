using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
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
                new DateOnly(2027, 3, 15));

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

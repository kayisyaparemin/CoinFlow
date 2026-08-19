using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;

namespace CoinFlow.Tests;

public sealed class SeedIntegrationTests
{
    [Fact]
    public async Task DevelopmentSeed_ProducesRequiredDemoSnapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: true);
            var service = CreateService(store, new DateOnly(2026, 8, 19));

            var dashboard = await service.GetDashboardAsync();
            var data = await service.GetFinanceDataAsync();

            Assert.Equal(115_000m, dashboard.SalaryPeriod.Salary);
            Assert.Equal(28_581.94m, dashboard.SalaryPeriod.TotalObligations);
            Assert.Equal(86_418.06m, dashboard.SalaryPeriod.SpendableBudget);
            Assert.Equal(86_418.06m, dashboard.DailyCoin.RemainingBudget);
            Assert.Equal(22, dashboard.DailyCoin.RemainingDays);
            Assert.Equal(3_928.09m, dashboard.DailyCoin.SustainableDailyBudget);

            Assert.Empty(data.PaymentPlans);
            Assert.Empty(data.Expenses);
            Assert.Collection(
                data.Loans,
                loan =>
                {
                    Assert.Equal("Garanti kredi", loan.Name);
                    Assert.Equal("Garanti", loan.Bank);
                    Assert.Equal(22, loan.InstallmentCount);
                    Assert.Equal(7, loan.PaymentDay);
                    Assert.Equal(14_501.23m, loan.MonthlyInstallment);
                },
                loan =>
                {
                    Assert.Equal("On Dijital", loan.Name);
                    Assert.Equal("Burgan", loan.Bank);
                    Assert.Equal(9, loan.InstallmentCount);
                    Assert.Equal(18, loan.PaymentDay);
                    Assert.Equal(7_374.59m, loan.MonthlyInstallment);
                });

            var card = Assert.Single(data.CreditCards);
            Assert.Equal("Axess", card.Name);
            Assert.Equal("Akbank", card.Bank);
            Assert.Equal(61_283.91m, card.CurrentCycleSpending);
            Assert.Equal(35_201.77m, card.LastStatementRemaining);
            Assert.Equal(123_751.49m, card.CurrentTotalDebt);
            Assert.Collection(
                card.FutureInstallments,
                installment =>
                {
                    Assert.Equal(new DateOnly(2026, 9, 28), installment.DueDate);
                    Assert.Equal(15_538.36m, installment.Amount);
                },
                installment =>
                {
                    Assert.Equal(new DateOnly(2026, 10, 30), installment.DueDate);
                    Assert.Equal(9_102.90m, installment.Amount);
                },
                installment =>
                {
                    Assert.Equal(new DateOnly(2026, 11, 28), installment.DueDate);
                    Assert.Equal(2_624.55m, installment.Amount);
                });
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync();
            }
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [Fact]
    public async Task StableEmptyDatabase_DoesNotReceiveDevelopmentFinanceData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: false);
            await store.InitializeAsync();

            Assert.Empty(await store.GetSalaryScheduleAsync());
            Assert.Empty(await store.GetLoansAsync());
            Assert.Empty(await store.GetCreditCardsAsync());
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync();
            }
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [Fact]
    public async Task SalaryCanBeAddedAfterFullReset()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: true);
            var service = CreateService(store, new DateOnly(2026, 8, 19));
            await service.ResetAllDataAsync();

            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                NetAmount = 120_000m,
                EffectiveFrom = new DateOnly(2026, 8, 1),
                Note = "Sıfırlama sonrası maaş"
            });

            var data = await service.GetFinanceDataAsync();
            var dashboard = await service.GetDashboardAsync();
            Assert.Equal(120_000m, Assert.Single(data.Salaries).NetAmount);
            Assert.Equal(120_000m, dashboard.SalaryPeriod.Salary);
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync();
            }
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [Fact]
    public async Task ResetAllData_RemovesEverythingAndDoesNotReseedAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: true);
            var service = CreateService(store, new DateOnly(2026, 8, 19));
            await service.AddExpenseAsync(new ExpenseDraft(
                250m,
                new DateOnly(2026, 8, 19),
                ExpenseCategory.Other,
                ExpensePaymentType.Cash,
                "Sıfırlama testi"));
            var seeded = await service.GetFinanceDataAsync();
            Assert.NotEmpty(seeded.Salaries);
            Assert.NotEmpty(seeded.Loans);
            Assert.NotEmpty(seeded.CreditCards);
            Assert.NotEmpty(seeded.Expenses);

            await service.ResetAllDataAsync();

            var reset = await service.GetFinanceDataAsync();
            Assert.Empty(reset.Salaries);
            Assert.Empty(reset.Loans);
            Assert.Empty(reset.PaymentPlans);
            Assert.Empty(reset.CreditCards);
            Assert.Empty(reset.Expenses);
            Assert.Equal(10, reset.Settings.SalaryDay);
            Assert.True(reset.Settings.GamificationEnabled);
            Assert.False(reset.Settings.DevelopmentSeedEnabled);
            Assert.Equal(0m, reset.EmergencyFund.TargetAmount);
            Assert.Equal(0m, reset.EmergencyFund.CurrentAmount);
            Assert.Equal(0m, reset.EmergencyFund.PlannedPeriodContribution);

            await store.DisposeAsync();
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: true);
            var reopened = await CreateService(store, new DateOnly(2026, 8, 19)).GetFinanceDataAsync();

            Assert.Empty(reopened.Salaries);
            Assert.Empty(reopened.Loans);
            Assert.Empty(reopened.PaymentPlans);
            Assert.Empty(reopened.CreditCards);
            Assert.Empty(reopened.Expenses);
            Assert.False(reopened.Settings.DevelopmentSeedEnabled);
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync();
            }
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [Fact]
    public async Task CardExpense_ChangesCardDebtButNotCurrentCashBudget()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: true);
            var service = CreateService(store, new DateOnly(2026, 8, 19));
            var before = await service.GetDashboardAsync();
            var card = Assert.Single((await service.GetFinanceDataAsync()).CreditCards);

            await service.AddExpenseAsync(new CoinFlow.Application.Models.ExpenseDraft(
                1_000m, new DateOnly(2026, 8, 19), ExpenseCategory.Car,
                ExpensePaymentType.CreditCard, "Tamir", card.Id));

            var after = await service.GetDashboardAsync();
            var updatedCard = Assert.Single((await service.GetFinanceDataAsync()).CreditCards);
            Assert.Equal(before.DailyCoin.RemainingBudget, after.DailyCoin.RemainingBudget);
            Assert.Equal(card.CurrentTotalDebt + 1_000m, updatedCard.CurrentTotalDebt);
        }
        finally
        {
            if (store is not null) await store.DisposeAsync();
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [Fact]
    public async Task EmergencyTransfer_IsRemovedFromCashBudgetAndAddedToBuffer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: true);
            var service = CreateService(store, new DateOnly(2026, 8, 19));
            var before = await service.GetDashboardAsync();

            await service.TransferToEmergencyFundAsync(1_000m);

            var after = await service.GetDashboardAsync();
            Assert.Equal(before.EmergencyFund.CurrentAmount + 1_000m, after.EmergencyFund.CurrentAmount);
            Assert.Equal(before.DailyCoin.RemainingBudget - 1_000m, after.DailyCoin.RemainingBudget);
        }
        finally
        {
            if (store is not null) await store.DisposeAsync();
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    [Fact]
    public async Task SeededCardSimulation_IncludesCurrentDebtsAndFutureInstallments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, seedDevelopmentData: true);
            var service = CreateService(store, new DateOnly(2026, 8, 19));
            var data = await service.GetFinanceDataAsync();
            var card = Assert.Single(data.CreditCards);

            var result = await service.SimulatePurchaseAsync(
                new PurchaseSimulationRequest(
                    "Test alışverişi",
                    30_000m,
                    PurchaseFundingMethod.CreditCard,
                    new DateOnly(2026, 8, 20),
                    3,
                    new DateOnly(2026, 10, 5),
                    card.Id));

            Assert.Equal(12, result.Rows.Count);
            Assert.Equal(28_581.94m, result.Rows[0].BaselineObligations);
            Assert.True(result.ExistingObligationsInHorizon > result.Rows[0].BaselineObligations);
            Assert.Contains(result.Rows, row => row.NewPayment > 0m);
            Assert.True(result.NewPaymentsInHorizon > 0m);
            Assert.True(result.RemainingNewDebtAfterHorizon >= 0m);
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync();
            }
            TryDelete(path);
            TryDelete(path + "-shm");
            TryDelete(path + "-wal");
        }
    }

    private static CoinFlowService CreateService(ICoinFlowStore store, DateOnly today) => new(
        store,
        new FixedClock(today),
        new SalaryPeriodCalculator(),
        new DailyCoinCalculator(),
        new CreditCardProjectionCalculator(),
        new PurchaseSimulationCalculator(new CreditCardProjectionCalculator()));

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed class FixedClock(DateOnly today) : IClock
    {
        public DateOnly Today { get; } = today;
    }
}

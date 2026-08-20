using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;
using SQLite;

namespace CoinFlow.Tests;

public sealed class PersistenceAndSeedTests
{
    private static readonly DateOnly Today = new(2026, 8, 20);

    [Fact]
    public async Task DevelopmentSeed_ContainsCanonicalRecords()
    {
        await WithStore(true, async store =>
        {
            var plan = await TestFactory.Service(store)
                .GetFinancialPlanAsync();

            Assert.Equal(10, plan.Settings.SalaryDay);
            Assert.Equal(30_000m, plan.Settings.MonthlyLivingBudget);
            Assert.Equal(0m, plan.Settings.ProjectionStartingSavings);
            Assert.Equal(
                [115_000m, 132_250m],
                plan.Salaries.Select(x => x.Amount).ToArray());

            var garanti = plan.Loans.Single(x =>
                x.Bank == "Garanti BBVA");
            Assert.Equal(14_501.23m, garanti.MonthlyPayment);
            Assert.Equal(22, garanti.RemainingInstallmentCount);
            Assert.Equal(190_188m, garanti.RemainingDebt);

            var burgan = plan.Loans.Single(x =>
                x.Bank == "Burgan Bank");
            Assert.Equal(7_374.59m, burgan.MonthlyPayment);
            Assert.Equal(9, burgan.RemainingInstallmentCount);
            Assert.Equal(55_777m, burgan.RemainingDebt);

            var eminevim = plan.PaymentPlans.Single(x =>
                x.Name == "Eminevim");
            Assert.Equal(3, eminevim.Installments.Count);
            Assert.Equal(
                111_827m,
                eminevim.Installments.Sum(x => x.Amount));
            Assert.DoesNotContain(
                eminevim.Installments,
                x => x.DueDate == new DateOnly(2026, 8, 20));

            var axess = Assert.Single(plan.CreditCards);
            Assert.Equal(607_350m, axess.Limit);
            Assert.Equal(35_201.77m, axess.CarriedBalance);
            Assert.Equal(61_283.91m, axess.UnbilledSpending);
            Assert.Equal(
                CreditCardPaymentStrategy.AskEachStatement,
                axess.PaymentStrategy);
            Assert.Equal(
                ProjectionFallbackStrategy.Minimum,
                axess.ProjectionFallbackStrategy);
            Assert.Empty(axess.PaymentPlans);
        });
    }

    [Fact]
    public async Task DevelopmentSeed_IsIdempotentAcrossReopen()
    {
        var path = TempPath();
        try
        {
            await using (var first = new SqliteCoinFlowStore(
                             path, true, Today))
            {
                var plan = await TestFactory.Service(first)
                    .GetFinancialPlanAsync();
                Assert.Equal(2, plan.Salaries.Count);
            }

            await using (var second = new SqliteCoinFlowStore(
                             path, true, Today))
            {
                var plan = await TestFactory.Service(second)
                    .GetFinancialPlanAsync();
                Assert.Equal(2, plan.Salaries.Count);
                Assert.Equal(2, plan.Loans.Count);
                Assert.Single(plan.PaymentPlans);
                Assert.Single(plan.CreditCards);
                Assert.Equal(3, plan.CreditCards[0].Charges.Count);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DevelopmentReset_ReloadsCanonicalSeed()
    {
        await WithStore(true, async store =>
        {
            var service = TestFactory.Service(store);
            var salary = (await service.GetFinancialPlanAsync())
                .Salaries[0];
            await service.DeleteSalaryAsync(salary.Id);

            await service.ResetDevelopmentDataAsync();
            var reset = await service.GetFinancialPlanAsync();

            Assert.Equal(2, reset.Salaries.Count);
            Assert.Single(reset.PaymentPlans);
            Assert.Equal(30_000m, reset.Settings.MonthlyLivingBudget);
        });
    }

    [Fact]
    public async Task ProductionEmptyDatabase_IsNotSeeded()
    {
        await WithStore(false, async store =>
        {
            var plan = await TestFactory.Service(store)
                .GetFinancialPlanAsync();

            Assert.Empty(plan.Salaries);
            Assert.Empty(plan.Loans);
            Assert.Empty(plan.PaymentPlans);
            Assert.Empty(plan.CreditCards);
            Assert.Equal(0m, plan.Settings.MonthlyLivingBudget);
        });
    }

    [Fact]
    public async Task NewIncomeAndLargeExpense_RoundTrip()
    {
        await WithStore(false, async store =>
        {
            var service = TestFactory.Service(store);
            var income = new OneTimeIncome
            {
                Description = "Bonus",
                Amount = 100_000m,
                ExactDate = new DateOnly(2027, 3, 15)
            };
            var expense = new PlannedLargeExpense
            {
                Name = "Tadilat",
                Amount = 350_000m,
                ExactDate = new DateOnly(2027, 3, 15),
                Note = "Plan"
            };

            await service.SaveOtherIncomeAsync(income);
            await service.SavePlannedLargeExpenseAsync(expense);
            var plan = await service.GetFinancialPlanAsync();

            Assert.Equal(income, Assert.Single(plan.OtherIncomes));
            Assert.Equal(expense, Assert.Single(plan.PlannedLargeExpenses));
        });
    }

    [Fact]
    public async Task ObsoleteDailyTrackingTables_AreRemovedOnUpgrade()
    {
        var path = TempPath();
        SQLitePCL.Batteries_V2.Init();
        var legacy = new SQLiteAsyncConnection(path);
        await legacy.ExecuteAsync(
            "CREATE TABLE expenses (Id TEXT PRIMARY KEY NOT NULL)");
        await legacy.ExecuteAsync(
            "CREATE TABLE spendable_balance_snapshots (Id TEXT PRIMARY KEY NOT NULL)");
        await legacy.CloseAsync();

        try
        {
            await using (var store = new SqliteCoinFlowStore(
                             path, false, Today))
            {
                await store.InitializeAsync();
            }

            var database = new SQLiteAsyncConnection(path);
            var tables = await database.QueryAsync<TableNameRow>(
                "SELECT name AS Name FROM sqlite_master WHERE type='table'");
            await database.CloseAsync();

            Assert.DoesNotContain(tables, x => x.Name == "expenses");
            Assert.DoesNotContain(
                tables,
                x => x.Name == "spendable_balance_snapshots");
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task LegacyCardAggregate_IsMigratedWithoutBalanceLoss()
    {
        var path = TempPath();
        SQLitePCL.Batteries_V2.Init();
        var legacy = new SQLiteAsyncConnection(path);
        await legacy.ExecuteAsync(
            """
            CREATE TABLE credit_cards (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL,
                Bank TEXT NOT NULL,
                [Limit] DECIMAL NOT NULL,
                CurrentTotalDebt DECIMAL NOT NULL,
                LastStatementDebt DECIMAL NOT NULL,
                LastStatementRemaining DECIMAL NOT NULL,
                CurrentCycleSpending DECIMAL NOT NULL,
                StatementClosingDay INTEGER NOT NULL,
                PaymentDueDay INTEGER NOT NULL,
                MinimumPaymentRate DECIMAL NOT NULL,
                PaymentMode INTEGER NOT NULL,
                ManualPaymentAmount DECIMAL NULL
            )
            """);
        var id = Guid.NewGuid();
        await legacy.ExecuteAsync(
            "INSERT INTO credit_cards VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            id.ToString("D"), "Legacy", "Banka", 200_000m,
            96_485.68m, 35_201.77m, 35_201.77m, 61_283.91m,
            25, 5, 0.40m, 1, 50_000m);
        await legacy.CloseAsync();

        try
        {
            await using var store = new SqliteCoinFlowStore(
                path, false, Today);
            var card = Assert.Single(
                (await TestFactory.Service(store)
                    .GetFinancialPlanAsync()).CreditCards);

            Assert.Equal(35_201.77m, card.CarriedBalance);
            Assert.Equal(61_283.91m, card.UnbilledSpending);
            Assert.Equal(Today, card.BalanceAsOfDate);
            Assert.Equal(
                CreditCardPaymentStrategy.AskEachStatement,
                card.PaymentStrategy);
            var payment = Assert.Single(card.PaymentPlans);
            Assert.Equal(new DateOnly(2026, 9, 5), payment.DueDate);
            Assert.Equal(50_000m, payment.Amount);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static async Task WithStore(
        bool seed,
        Func<SqliteCoinFlowStore, Task> test)
    {
        var path = TempPath();
        var store = new SqliteCoinFlowStore(
            path,
            seed,
            Today);
        try
        {
            await test(store);
        }
        finally
        {
            await store.DisposeAsync();
            DeleteDatabase(path);
        }
    }

    private static string TempPath() => Path.Combine(
        Path.GetTempPath(),
        $"coinflow-{Guid.NewGuid():N}.db");

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

    private sealed class TableNameRow
    {
        public string Name { get; set; } = string.Empty;
    }
}

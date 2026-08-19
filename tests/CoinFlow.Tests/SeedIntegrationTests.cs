using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using CoinFlow.Infrastructure.Persistence;
using SQLite;

namespace CoinFlow.Tests;

public sealed class SeedIntegrationTests
{
    private static readonly DateOnly Today = new(2026, 8, 19);
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DevelopmentSeed_UsesCurrentActualSnapshotAndExactCardStatements()
    {
        await WithStore(seed: true, async store =>
        {
            var service = CreateService(store);

            var dashboard = await service.GetDashboardAsync();
            var data = await service.GetFinanceDataAsync();
            var card = Assert.Single(data.CreditCards);
            var firstStatement = new CreditCardProjectionCalculator().Project(card, 1)[0];

            Assert.Equal(11_000m, dashboard.DailyCoin.RemainingBudget);
            Assert.Equal(22, dashboard.DailyCoin.RemainingDays);
            Assert.Equal(500m, dashboard.DailyCoin.BaseDailyCoin);
            Assert.Equal(500m, dashboard.DailyCoin.SustainableDailyBudget);
            Assert.Equal(53_095.50m, dashboard.SalaryPeriod.TotalObligations);
            Assert.Equal(61_904.50m, dashboard.SalaryPeriod.SpendableBudget);
            Assert.Equal(new DateOnly(2026, 8, 25), firstStatement.StatementCloseDate);
            Assert.Equal(new DateOnly(2026, 9, 5), firstStatement.PaymentDueDate);
            Assert.Equal(96_485.68m, firstStatement.StatementBalance);
            Assert.Equal(38_594.27m, firstStatement.Payment);
            Assert.Equal(123_751.49m, card.CurrentTotalDebt);
            Assert.Contains(card.Charges, x => x.PostingDate == new DateOnly(2026, 9, 28));
        });
    }

    [Fact]
    public async Task FuturePeriods_FirstRowUsesActualAndFutureRowsUseProjection()
    {
        await WithStore(seed: true, async store =>
        {
            var rows = await CreateService(store).GetFutureMonthsAsync();

            Assert.True(rows[0].IsCurrentActual);
            Assert.Equal(11_000m, rows[0].Spendable);
            Assert.False(rows[1].IsCurrentActual);
            Assert.Equal(rows[1].ProjectedSpendable, rows[1].Spendable);
        });
    }

    [Fact]
    public async Task CashExpenseReducesActualButCardAndInstallmentDoNot()
    {
        await WithStore(seed: true, async store =>
        {
            var service = CreateService(store);
            var card = Assert.Single((await service.GetFinanceDataAsync()).CreditCards);

            await service.AddExpenseAsync(new ExpenseDraft(
                1_000m, Today, ExpenseCategory.Food, ExpensePaymentType.Cash, "Nakit"));
            await service.AddExpenseAsync(new ExpenseDraft(
                2_000m, Today, ExpenseCategory.Home, ExpensePaymentType.CreditCard, "Kart", card.Id));
            await service.AddExpenseAsync(new ExpenseDraft(
                3_000m, Today, ExpenseCategory.Car, ExpensePaymentType.NewInstallment, "Taksit",
                InstallmentCount: 3,
                FirstInstallmentDate: new DateOnly(2026, 9, 20)));

            var dashboard = await service.GetDashboardAsync();
            var data = await service.GetFinanceDataAsync();
            var updatedCard = Assert.Single(data.CreditCards);
            Assert.Equal(10_000m, dashboard.DailyCoin.RemainingBudget);
            Assert.Contains(updatedCard.Charges, x => x.PostingDate == Today && x.Amount == 2_000m);
            var plan = Assert.Single(data.PaymentPlans);
            Assert.Equal(3_000m, plan.Installments.Sum(x => x.Amount));
        });
    }

    [Fact]
    public async Task PlannedEmergencyTransfer_DoesNotReduceActualTwice()
    {
        await WithStore(seed: true, async store =>
        {
            var service = CreateService(store);
            var data = await service.GetFinanceDataAsync();
            await service.SaveEmergencyFundAsync(data.EmergencyFund with
            {
                TargetAmount = 200_000m,
                CurrentAmount = 100_000m,
                PlannedPeriodContribution = 20_000m
            });

            await service.TransferToEmergencyFundAsync(20_000m);

            var dashboard = await service.GetDashboardAsync();
            Assert.Equal(11_000m, dashboard.DailyCoin.RemainingBudget);
            Assert.Equal(120_000m, dashboard.EmergencyFund.CurrentAmount);
        });
    }

    [Fact]
    public async Task PaperAcceptanceCase_Produces87767And27233And90777()
    {
        await WithStore(seed: false, async store =>
        {
            var service = CreateService(store, new DateOnly(2026, 9, 10));
            await service.SaveSettingsAsync(new UserSettings
            {
                SalaryDay = 10,
                TrackingStartedDate = new DateOnly(2026, 9, 10)
            });
            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                NetAmount = 115_000m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            });
            await service.SaveLoanAsync(new Loan
            {
                Name = "Garanti",
                MonthlyInstallment = 14_500m,
                PaymentDay = 7,
                StartDate = new DateOnly(2026, 10, 7),
                InstallmentCount = 1
            });
            await service.SaveLoanAsync(new Loan
            {
                Name = "Burgan",
                MonthlyInstallment = 7_500m,
                PaymentDay = 18,
                StartDate = new DateOnly(2026, 9, 18),
                InstallmentCount = 1
            });
            var planId = Guid.NewGuid();
            await service.SavePaymentPlanAsync(new TemporaryPaymentPlan
            {
                Id = planId,
                Name = "Geçici finansman",
                Kind = PaymentPlanKind.Temporary,
                Installments =
                [
                    new TemporaryPaymentInstallment
                    {
                        PlanId = planId,
                        DueDate = new DateOnly(2026, 9, 20),
                        Amount = 28_167m
                    }
                ]
            });
            await service.SaveCreditCardAsync(new CreditCard
            {
                Name = "Kağıt hesabı",
                Limit = 200_000m,
                CarriedBalance = 35_000m,
                UnbilledSpending = 59_000m,
                BalanceAsOfDate = new DateOnly(2026, 9, 1),
                StatementClosingDay = 25,
                PaymentDueDay = 5,
                MinimumPaymentRate = 0.40m
            });

            var row = Assert.Single(await service.GetFutureMonthsAsync(new DateOnly(2026, 9, 10), 1));

            Assert.Equal(87_767m, row.TotalObligations);
            Assert.Equal(27_233m, row.ProjectedSpendable);
            Assert.Equal(907.77m, row.ProjectedDailyCoin);
        });
    }

    [Fact]
    public async Task ResetAllData_RemovesSnapshotsAndDoesNotReseed()
    {
        var path = TempPath();
        SqliteCoinFlowStore? store = null;
        try
        {
            store = new SqliteCoinFlowStore(path, true, Today);
            var service = CreateService(store);
            Assert.NotEmpty((await service.GetFinanceDataAsync()).SpendableBalanceSnapshots);

            await service.ResetAllDataAsync();
            var reset = await service.GetFinanceDataAsync();

            Assert.Empty(reset.Salaries);
            Assert.Empty(reset.CreditCards);
            Assert.Empty(reset.SpendableBalanceSnapshots);
            Assert.False(reset.Settings.DevelopmentSeedEnabled);

            await store.DisposeAsync();
            store = new SqliteCoinFlowStore(path, true, Today);
            var reopened = await CreateService(store).GetFinanceDataAsync();
            Assert.Empty(reopened.Salaries);
            Assert.Empty(reopened.SpendableBalanceSnapshots);
        }
        finally
        {
            if (store is not null) await store.DisposeAsync();
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task SalaryCanBeAddedAfterReset()
    {
        await WithStore(seed: true, async store =>
        {
            var service = CreateService(store);
            await service.ResetAllDataAsync();
            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                NetAmount = 120_000m,
                EffectiveFrom = new DateOnly(2026, 8, 1)
            });

            Assert.Equal(120_000m, Assert.Single((await service.GetFinanceDataAsync()).Salaries).NetAmount);
        });
    }

    [Fact]
    public async Task FutureProjection_DoesNotClampNegativeFreeBudget()
    {
        await WithStore(seed: false, async store =>
        {
            var service = CreateService(store);
            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                NetAmount = 100m,
                EffectiveFrom = new DateOnly(2026, 1, 1)
            });
            await service.SaveLoanAsync(new Loan
            {
                Name = "Büyük taksit",
                MonthlyInstallment = 500m,
                PaymentDay = 7,
                StartDate = new DateOnly(2026, 9, 7),
                InstallmentCount = 1
            });

            var row = Assert.Single(await service.GetFutureMonthsAsync(Today, 1));

            Assert.Equal(-400m, row.ProjectedSpendable);
        });
    }

    [Fact]
    public async Task LegacyCardAggregate_IsMigratedWithoutLosingBalances()
    {
        var path = TempPath();
        SQLitePCL.Batteries_V2.Init();
        var legacy = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
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
            id.ToString("D"), "Legacy", "Banka", 200_000m, 96_485.68m,
            35_201.77m, 35_201.77m, 61_283.91m, 25, 5, 0.40m, 0, null);
        await legacy.CloseAsync();

        var store = new SqliteCoinFlowStore(path, false, Today);
        try
        {
            var card = Assert.Single((await CreateService(store).GetFinanceDataAsync()).CreditCards);

            Assert.Equal(35_201.77m, card.CarriedBalance);
            Assert.Equal(61_283.91m, card.UnbilledSpending);
            Assert.Equal(Today, card.BalanceAsOfDate);
        }
        finally
        {
            await store.DisposeAsync();
            DeleteDatabase(path);
        }
    }

    private static async Task WithStore(bool seed, Func<SqliteCoinFlowStore, Task> test)
    {
        var path = TempPath();
        var store = new SqliteCoinFlowStore(path, seed, Today);
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

    private static CoinFlowService CreateService(ICoinFlowStore store, DateOnly? today = null)
    {
        var salary = new SalaryPeriodCalculator();
        var loan = new LoanScheduleCalculator();
        var card = new CreditCardProjectionCalculator();
        var mandatory = new MandatoryPaymentCalculator(loan);
        var spendable = new SpendableBalanceCalculator();
        var daily = new DailyCoinCalculator();
        var emergency = new EmergencyFundCalculator();
        var installments = new InstallmentScheduleCalculator();
        var projection = new FinancialProjectionService(
            salary,
            loan,
            card,
            mandatory,
            spendable,
            daily,
            emergency);
        return new CoinFlowService(
            store,
            new FixedClock(today ?? Today, Now),
            salary,
            projection,
            new PurchaseSimulationCalculator(card, installments),
            installments,
            emergency);
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"coinflow-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string path)
    {
        foreach (var candidate in new[] { path, path + "-shm", path + "-wal" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private sealed class FixedClock(DateOnly today, DateTimeOffset utcNow) : IClock
    {
        public DateOnly Today { get; } = today;
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

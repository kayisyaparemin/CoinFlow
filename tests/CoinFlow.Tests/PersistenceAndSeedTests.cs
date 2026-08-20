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
            var service = TestFactory.Service(store);
            await service.LoadCanonicalDevelopmentDataAsync();
            var plan = await service.GetFinancialPlanAsync();

            Assert.Equal(10, plan.Settings.SalaryDay);
            Assert.Equal(30_000m, plan.Settings.MonthlyLivingBudget);
            Assert.Equal(0m, plan.Settings.ProjectionStartingSavings);
            Assert.Equal(Today, plan.Settings.ProjectionAnchorDate);
            Assert.Equal(
                PaymentAssignmentMode.UpcomingPeriod,
                Assert.Single(plan.PaymentAssignmentStrategies).Mode);
            Assert.Equal(
                new DateOnly(2026, 9, 10),
                plan.PaymentAssignmentStrategies[0]
                    .EffectiveFromSalaryDate);
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
                var service = TestFactory.Service(first);
                await service.LoadCanonicalDevelopmentDataAsync();
                await service.LoadCanonicalDevelopmentDataAsync();
                var plan = await service.GetFinancialPlanAsync();
                Assert.Equal(2, plan.Salaries.Count);
            }

            await using (var second = new SqliteCoinFlowStore(
                             path, true, Today))
            {
                var service = TestFactory.Service(second);
                await service.LoadCanonicalDevelopmentDataAsync();
                var plan = await service.GetFinancialPlanAsync();
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
    public async Task DevelopmentSeed_UpsertsCanonicalDataIntoExistingDatabase()
    {
        await WithStore(true, async store =>
        {
            var service = TestFactory.Service(store);
            await service.SaveOtherIncomeAsync(new OneTimeIncome
            {
                Description = "Kullanıcı bonusu",
                Amount = 5_000m,
                ExactDate = new DateOnly(2026, 10, 1)
            });
            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                Amount = 150_000m,
                EffectiveDate = new DateOnly(2025, 1, 1),
                Description = "Kullanıcı maaşı"
            });
            await service.CompleteInitialPaymentStrategySetupAsync(
                PaymentAssignmentMode.PreviousPeriod);

            await service.LoadCanonicalDevelopmentDataAsync();
            await service.LoadCanonicalDevelopmentDataAsync();
            var plan = await service.GetFinancialPlanAsync();

            Assert.Equal(3, plan.Salaries.Count);
            Assert.Single(plan.OtherIncomes);
            Assert.Equal("Kullanıcı bonusu", plan.OtherIncomes[0].Description);
            Assert.Equal(2, plan.Loans.Count);
            Assert.Single(plan.PaymentPlans);
            Assert.Single(plan.CreditCards);
            var initial = Assert.Single(plan.PaymentAssignmentStrategies);
            Assert.Equal(PaymentAssignmentMode.UpcomingPeriod, initial.Mode);
            Assert.Equal(new DateOnly(2026, 9, 10),
                initial.EffectiveFromSalaryDate);
        });
    }

    [Fact]
    public async Task FirstSalary_InitializesAnchorOnce_AndCreatesOneChosenStrategy()
    {
        var path = TempPath();
        try
        {
            await using (var first = new SqliteCoinFlowStore(
                             path, false, Today))
            {
                var service = TestFactory.Service(first);
                var empty = await service.GetFinancialPlanAsync();
                Assert.Empty(empty.Salaries);
                Assert.Empty(empty.PaymentAssignmentStrategies);
                Assert.Equal(default, empty.Settings.ProjectionAnchorDate);

                var setup = await service.SaveSalaryAsync(
                    new SalaryScheduleEntry
                    {
                        Amount = 100_000m,
                        EffectiveDate = Today,
                        Description = "Maaş"
                    });
                Assert.NotNull(setup);
                Assert.Equal(
                    Today,
                    (await first.GetSettingsAsync()).ProjectionAnchorDate);
                Assert.Equal(
                    new DateOnly(2026, 9, 10),
                    setup!.EffectiveSalaryDate);
                Assert.Empty((await service.GetFinancialPlanAsync())
                    .PaymentAssignmentStrategies);

                await service.CompleteInitialPaymentStrategySetupAsync(
                    PaymentAssignmentMode.PreviousPeriod);
                var strategy = Assert.Single(
                    (await service.GetFinancialPlanAsync())
                    .PaymentAssignmentStrategies);
                Assert.Equal(PaymentAssignmentMode.PreviousPeriod, strategy.Mode);
                Assert.Equal(new DateOnly(2026, 9, 10),
                    strategy.EffectiveFromSalaryDate);
                Assert.NotNull(await service.GetDashboardAsync());
            }

            await using var second = new SqliteCoinFlowStore(
                path,
                false,
                Today.AddDays(45));
            Assert.Equal(
                Today,
                (await second.GetSettingsAsync()).ProjectionAnchorDate);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DevelopmentClear_LeavesValidEmptyState_AndDoesNotReseed()
    {
        await WithStore(true, async store =>
        {
            var service = TestFactory.Service(store);
            await service.LoadCanonicalDevelopmentDataAsync();
            var salary = (await service.GetFinancialPlanAsync())
                .Salaries[0];
            await service.DeleteSalaryAsync(salary.Id);
            var strategy = Assert.Single(
                (await service.GetFinancialPlanAsync())
                .PaymentAssignmentStrategies);
            await service.SavePaymentAssignmentStrategyAsync(strategy with
            {
                Mode = PaymentAssignmentMode.PreviousPeriod
            });

            await service.ClearDevelopmentDataAsync();
            var reset = await service.GetFinancialPlanAsync();

            Assert.Empty(reset.Salaries);
            Assert.Empty(reset.OtherIncomes);
            Assert.Empty(reset.Loans);
            Assert.Empty(reset.PaymentPlans);
            Assert.Empty(reset.CreditCards);
            Assert.Empty(reset.PlannedLargeExpenses);
            Assert.Empty(reset.PaymentAssignmentStrategies);
            Assert.Equal(0m, reset.Settings.MonthlyLivingBudget);
            Assert.Equal(0m, reset.Settings.ProjectionStartingSavings);
            Assert.Equal(default, reset.Settings.ProjectionAnchorDate);
            Assert.Null(await service.GetDashboardAsync());
            Assert.Empty(await service.GetFuturePeriodsAsync());

            await service.LoadCanonicalDevelopmentDataAsync();
            var seeded = await service.GetFinancialPlanAsync();
            Assert.Equal(2, seeded.Salaries.Count);
            Assert.Single(seeded.PaymentAssignmentStrategies);
        });
    }

    [Fact]
    public async Task DevelopmentDatabase_DoesNotSeedAutomatically()
    {
        await WithStore(true, async store =>
        {
            var service = TestFactory.Service(store);
            var plan = await service.GetFinancialPlanAsync();

            Assert.Empty(plan.Salaries);
            Assert.Empty(plan.Loans);
            Assert.Empty(plan.CreditCards);
            Assert.Empty(plan.PaymentAssignmentStrategies);
            Assert.Equal(default, plan.Settings.ProjectionAnchorDate);
            Assert.Null(await service.GetDashboardAsync());
            Assert.Empty(await service.GetFuturePeriodsAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SimulateAsync(new SimulationRequest(
                    SimulationScenarioType.CashPurchase,
                    "Test",
                    1_000m,
                    Today)));
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
            Assert.Equal(default, plan.Settings.ProjectionAnchorDate);
            Assert.Empty(plan.PaymentAssignmentStrategies);
        });
    }

    [Fact]
    public async Task PaymentAssignmentStrategy_PersistsAcrossReopen()
    {
        var path = TempPath();
        try
        {
            await using (var first = new SqliteCoinFlowStore(
                             path, false, Today))
            {
                var service = TestFactory.Service(first);
                await service.SaveSalaryAsync(new SalaryScheduleEntry
                {
                    Amount = 100_000m,
                    EffectiveDate = Today,
                    Description = "Maaş"
                });
                await service.CompleteInitialPaymentStrategySetupAsync(
                    PaymentAssignmentMode.PreviousPeriod);
            }

            await using var second = new SqliteCoinFlowStore(
                path, false, Today);
            var reopened = Assert.Single(
                (await TestFactory.Service(second)
                    .GetFinancialPlanAsync())
                .PaymentAssignmentStrategies);

            Assert.Equal(
                PaymentAssignmentMode.PreviousPeriod,
                reopened.Mode);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task StrategyPreview_DoesNotMutateHistory_AndFutureRecordCanBeDeleted()
    {
        await WithStore(true, async store =>
        {
            var service = TestFactory.Service(store);
            await service.LoadCanonicalDevelopmentDataAsync();
            var before = await service.GetFinancialPlanAsync();

            var preview = await service.PreviewPaymentAssignmentStrategyAsync(
                PaymentAssignmentMode.PreviousPeriod,
                new DateOnly(2026, 12, 10));
            var afterPreview = await service.GetFinancialPlanAsync();

            Assert.Single(before.PaymentAssignmentStrategies);
            Assert.Single(afterPreview.PaymentAssignmentStrategies);
            Assert.Equal(new DateOnly(2026, 12, 10),
                preview.EffectiveSalaryDate);

            var future = new PaymentAssignmentStrategy
            {
                Mode = PaymentAssignmentMode.PreviousPeriod,
                EffectiveFromSalaryDate = new DateOnly(2026, 12, 10),
                Note = "Planlı değişiklik"
            };
            await service.SavePaymentAssignmentStrategyAsync(future);
            Assert.Equal(2, (await service.GetFinancialPlanAsync())
                .PaymentAssignmentStrategies.Count);

            await service.DeletePaymentAssignmentStrategyAsync(future.Id);
            Assert.Single((await service.GetFinancialPlanAsync())
                .PaymentAssignmentStrategies);
        });
    }

    [Fact]
    public async Task LaterStrategyChanges_InsertEvents_AndPreserveEarlierHistory()
    {
        await WithStore(false, async store =>
        {
            var service = TestFactory.Service(store);
            await service.SaveSalaryAsync(new SalaryScheduleEntry
            {
                Amount = 100_000m,
                EffectiveDate = Today,
                Description = "Maaş"
            });
            await service.CompleteInitialPaymentStrategySetupAsync(
                PaymentAssignmentMode.PreviousPeriod);
            var initial = Assert.Single(
                (await service.GetFinancialPlanAsync())
                .PaymentAssignmentStrategies);

            await service.SavePaymentAssignmentStrategyAsync(
                new PaymentAssignmentStrategy
                {
                    Mode = PaymentAssignmentMode.UpcomingPeriod,
                    EffectiveFromSalaryDate = new DateOnly(2026, 12, 10),
                    Note = "İkinci karar"
                });
            await service.SavePaymentAssignmentStrategyAsync(
                new PaymentAssignmentStrategy
                {
                    Mode = PaymentAssignmentMode.PreviousPeriod,
                    EffectiveFromSalaryDate = new DateOnly(2027, 4, 10),
                    Note = "Üçüncü karar"
                });

            var history = (await service.GetFinancialPlanAsync())
                .PaymentAssignmentStrategies;
            Assert.Collection(
                history,
                first => Assert.Equal(initial, first),
                second =>
                {
                    Assert.Equal(new DateOnly(2026, 12, 10),
                        second.EffectiveFromSalaryDate);
                    Assert.Equal(PaymentAssignmentMode.UpcomingPeriod,
                        second.Mode);
                },
                third =>
                {
                    Assert.Equal(new DateOnly(2027, 4, 10),
                        third.EffectiveFromSalaryDate);
                    Assert.Equal(PaymentAssignmentMode.PreviousPeriod,
                        third.Mode);
                });
        });
    }

    [Fact]
    public async Task HistoricalStrategyCorrection_RequiresExplicitConfirmation()
    {
        await WithStore(true, async store =>
        {
            var service = TestFactory.Service(
                store,
                new DateOnly(2026, 9, 20));
            await service.LoadCanonicalDevelopmentDataAsync();
            var initial = Assert.Single(
                (await service.GetFinancialPlanAsync())
                .PaymentAssignmentStrategies);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SavePaymentAssignmentStrategyAsync(initial with
                {
                    Mode = PaymentAssignmentMode.PreviousPeriod
                }));

            await service.SavePaymentAssignmentStrategyAsync(initial with
            {
                Mode = PaymentAssignmentMode.PreviousPeriod
            }, confirmedHistoricalCorrection: true);
            Assert.Equal(
                PaymentAssignmentMode.PreviousPeriod,
                Assert.Single((await service.GetFinancialPlanAsync())
                    .PaymentAssignmentStrategies).Mode);
        });
    }

    [Fact]
    public async Task LegacySettingsMigration_DefaultsModeOnceAndPreservesValues()
    {
        var path = TempPath();
        SQLitePCL.Batteries_V2.Init();
        var legacy = new SQLiteAsyncConnection(path);
        await legacy.ExecuteAsync(
            """
            CREATE TABLE settings (
                Id INTEGER PRIMARY KEY NOT NULL,
                SalaryDay INTEGER NOT NULL,
                MonthlyLivingBudget DECIMAL NOT NULL,
                ProjectionStartingSavings DECIMAL NOT NULL,
                PaymentAssignmentMode INTEGER NOT NULL,
                SchemaVersion INTEGER NOT NULL,
                DevelopmentSeedVersion INTEGER NOT NULL,
                GamificationEnabled INTEGER NOT NULL,
                DevelopmentSeedEnabled INTEGER NOT NULL,
                TrackingStartedDate TEXT NULL
            )
            """);
        await legacy.ExecuteAsync(
            "INSERT INTO settings VALUES (1, 15, 42000, 123000, 1, 5, 1, 0, 0, NULL)");
        await legacy.CloseAsync();

        try
        {
            await using var store = new SqliteCoinFlowStore(
                path, false, Today);
            var settings = (await TestFactory.Service(store)
                .GetFinancialPlanAsync()).Settings;

            Assert.Equal(15, settings.SalaryDay);
            Assert.Equal(42_000m, settings.MonthlyLivingBudget);
            Assert.Equal(123_000m, settings.ProjectionStartingSavings);
            Assert.Equal(Today, settings.ProjectionAnchorDate);
            var strategy = Assert.Single(
                (await TestFactory.Service(store)
                    .GetFinancialPlanAsync())
                .PaymentAssignmentStrategies);
            Assert.Equal(
                PaymentAssignmentMode.PreviousPeriod,
                strategy.Mode);
            Assert.Equal(new DateOnly(2026, 9, 15),
                strategy.EffectiveFromSalaryDate);
        }
        finally
        {
            DeleteDatabase(path);
        }
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

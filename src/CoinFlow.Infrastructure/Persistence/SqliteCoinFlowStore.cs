using System.Globalization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Models;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

public sealed class SqliteCoinFlowStore : ICoinFlowStore, IAsyncDisposable
{
    private const string DateFormat = "yyyy-MM-dd";
    private const int CurrentSchemaVersion = 9;
    private const int CurrentCardStatementModelVersion = 6;
    private const decimal DefaultPlanningInterestRate = 0.05m;
    private static readonly Guid LegacyInitialAssignmentStrategyId =
        Guid.Parse("50000000-0000-0000-0000-000000000001");
    private readonly SQLiteAsyncConnection _database;
    private readonly bool _developmentFeaturesEnabled;
    private readonly DateOnly _migrationDate;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public SqliteCoinFlowStore(
        string databasePath,
        bool developmentFeaturesEnabled,
        DateOnly migrationDate)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "Veritabanı yolu gereklidir.",
                nameof(databasePath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        SQLitePCL.Batteries_V2.Init();
        _database = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.Create |
            SQLiteOpenFlags.SharedCache);
        _developmentFeaturesEnabled = developmentFeaturesEnabled;
        _migrationDate = migrationDate;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _database.CreateTableAsync<SalaryRow>();
            await _database.CreateTableAsync<OtherIncomeRow>();
            await _database.CreateTableAsync<LoanRow>();
            await _database.CreateTableAsync<PaymentPlanRow>();
            await _database.CreateTableAsync<PaymentInstallmentRow>();
            await _database.CreateTableAsync<CreditCardRow>();
            await _database.CreateTableAsync<CardInstallmentRow>();
            await _database.CreateTableAsync<CreditCardPaymentPlanRow>();
            await _database.CreateTableAsync<PlannedLargeExpenseRow>();
            await _database.CreateTableAsync<SettingsRow>();
            await _database.CreateTableAsync<PaymentAssignmentStrategyRow>();
            await _database.CreateTableAsync<FinancialSnapshotRow>();
            await _database.CreateTableAsync<PeriodPlanSnapshotRow>();
            await _database.CreateTableAsync<PeriodPlanPaymentLineRow>();
            await _database.CreateTableAsync<PeriodPlanRevisionRow>();
            await _database.CreateTableAsync<PeriodPlanRevisionPaymentLineRow>();
            await _database.CreateTableAsync<PeriodActualRow>();
            await _database.CreateTableAsync<ActualPaymentRow>();
            await _database.CreateTableAsync<ActualFlowRow>();
            await _database.CreateTableAsync<ActualLivingBreakdownRow>();

            await MigratePeriodPlanRevisionSchemaAsync();
            await MigrateLegacyCreditCardsAsync();
            await RemoveObsoleteDailyTrackingTablesAsync();

            var settings = await _database
                .Table<SettingsRow>()
                .FirstOrDefaultAsync();
            var isNewSettings = settings is null;
            if (settings is null)
            {
                settings = DefaultSettingsRow();
                await _database.InsertAsync(settings);
            }

            var needsStrategyMigration = !isNewSettings &&
                                         settings.SchemaVersion <
                                         CurrentSchemaVersion;
            if (needsStrategyMigration &&
                string.IsNullOrWhiteSpace(settings.ProjectionAnchorDate))
            {
                settings.ProjectionAnchorDate = FormatDate(_migrationDate);
            }

            if (needsStrategyMigration)
            {
                await EnsureInitialPaymentAssignmentStrategyAsync(settings);
            }

            if (!isNewSettings && settings.SchemaVersion < 7)
            {
                settings.CreditCardCarryInterestRate =
                    DefaultPlanningInterestRate;
                settings.DeficitFinancingInterestRate =
                    DefaultPlanningInterestRate;
            }

            settings.SchemaVersion = CurrentSchemaVersion;
            settings.DevelopmentSeedEnabled = _developmentFeaturesEnabled;
            settings.LegacyRemovedFeatureFlag = false;
            settings.TrackingStartedDate = null;
            await _database.UpdateAsync(settings);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task ClearAllFinancialDataAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await _database.RunInTransactionAsync(connection =>
        {
            connection.DeleteAll<ActualLivingBreakdownRow>();
            connection.DeleteAll<ActualFlowRow>();
            connection.DeleteAll<ActualPaymentRow>();
            connection.DeleteAll<PeriodActualRow>();
            connection.DeleteAll<PeriodPlanRevisionPaymentLineRow>();
            connection.DeleteAll<PeriodPlanRevisionRow>();
            connection.DeleteAll<PeriodPlanPaymentLineRow>();
            connection.DeleteAll<PeriodPlanSnapshotRow>();
            connection.DeleteAll<FinancialSnapshotRow>();
            connection.DeleteAll<PaymentInstallmentRow>();
            connection.DeleteAll<PaymentPlanRow>();
            connection.DeleteAll<CardInstallmentRow>();
            connection.DeleteAll<CreditCardPaymentPlanRow>();
            connection.DeleteAll<CreditCardRow>();
            connection.DeleteAll<PlannedLargeExpenseRow>();
            connection.DeleteAll<OtherIncomeRow>();
            connection.DeleteAll<SalaryRow>();
            connection.DeleteAll<LoanRow>();
            connection.DeleteAll<PaymentAssignmentStrategyRow>();
            var settings = connection.Table<SettingsRow>().First();
            settings.SalaryDay = 10;
            settings.MonthlyLivingBudget = 0m;
            settings.ProjectionStartingSavings = 0m;
            settings.ProjectionAnchorDate = null;
            settings.CreditCardCarryInterestRate =
                DefaultPlanningInterestRate;
            settings.DeficitFinancingInterestRate =
                DefaultPlanningInterestRate;
            settings.PaymentAssignmentMode =
                (int)PaymentAssignmentMode.UpcomingPeriod;
            settings.DevelopmentSeedVersion = 0;
            settings.SchemaVersion = CurrentSchemaVersion;
            connection.Update(settings);
        });
    }

    public async Task LoadCanonicalDevelopmentDataAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (!_developmentFeaturesEnabled)
        {
            throw new InvalidOperationException(
                "Test verisi yalnızca geliştirme sürümünde yüklenebilir.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await DevelopmentDataSeeder.SeedAsync(_database);
        var settings = await _database.Table<SettingsRow>().FirstAsync();
        settings.SalaryDay = 10;
        settings.MonthlyLivingBudget = 30_000m;
        settings.ProjectionStartingSavings = 0m;
        settings.ProjectionAnchorDate = FormatDate(
            new DateOnly(2026, 8, 20));
        settings.CreditCardCarryInterestRate =
            DefaultPlanningInterestRate;
        settings.DeficitFinancingInterestRate =
            DefaultPlanningInterestRate;
        settings.PaymentAssignmentMode =
            (int)PaymentAssignmentMode.UpcomingPeriod;
        settings.DevelopmentSeedVersion =
            DevelopmentDataSeeder.CurrentSeedVersion;
        await _database.UpdateAsync(settings);
    }

    public async Task<UserSettings> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<SettingsRow>().FirstAsync();
        return new UserSettings
        {
            SalaryDay = row.SalaryDay,
            MonthlyLivingBudget = row.MonthlyLivingBudget,
            ProjectionStartingSavings = row.ProjectionStartingSavings,
            CreditCardCarryInterestRate =
                row.CreditCardCarryInterestRate,
            DeficitFinancingInterestRate =
                row.DeficitFinancingInterestRate,
            ProjectionAnchorDate = string.IsNullOrWhiteSpace(
                row.ProjectionAnchorDate)
                ? default
                : ParseDate(row.ProjectionAnchorDate)
        };
    }

    public async Task SaveSettingsAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<SettingsRow>().FirstAsync();
        row.SalaryDay = settings.SalaryDay;
        row.MonthlyLivingBudget = settings.MonthlyLivingBudget;
        row.ProjectionStartingSavings =
            settings.ProjectionStartingSavings;
        row.CreditCardCarryInterestRate =
            settings.CreditCardCarryInterestRate;
        row.DeficitFinancingInterestRate =
            settings.DeficitFinancingInterestRate;
        row.ProjectionAnchorDate = settings.ProjectionAnchorDate == default
            ? null
            : FormatDate(settings.ProjectionAnchorDate);
        await _database.UpdateAsync(row);
    }

    public async Task<IReadOnlyList<PaymentAssignmentStrategy>>
        GetPaymentAssignmentStrategiesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database
                .Table<PaymentAssignmentStrategyRow>()
                .OrderBy(x => x.EffectiveFromSalaryDate)
                .ToListAsync())
            .Select(FromRow)
            .ToArray();
    }

    public async Task UpsertPaymentAssignmentStrategyAsync(
        PaymentAssignmentStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(strategy));
    }

    public async Task DeletePaymentAssignmentStrategyAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.DeleteAsync<PaymentAssignmentStrategyRow>(
            Key(id));
    }

    public async Task<IReadOnlyList<SalaryScheduleEntry>>
        GetSalaryScheduleAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<SalaryRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.EffectiveDate)
            .ToArray();
    }

    public async Task UpsertSalaryAsync(
        SalaryScheduleEntry entry,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(entry));
    }

    public async Task DeleteSalaryAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM salary_schedule WHERE Id = ?",
            Key(id));
    }

    public async Task<IReadOnlyList<OneTimeIncome>>
        GetOtherIncomesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<OtherIncomeRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.ExactDate)
            .ToArray();
    }

    public async Task UpsertOtherIncomeAsync(
        OneTimeIncome income,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(income));
    }

    public async Task DeleteOtherIncomeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM other_incomes WHERE Id = ?",
            Key(id));
    }

    public async Task<IReadOnlyList<Loan>> GetLoansAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<LoanRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.NextPaymentDate)
            .ToArray();
    }

    public async Task UpsertLoanAsync(
        Loan loan,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(loan));
    }

    public async Task DeleteLoanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM loans WHERE Id = ?",
            Key(id));
    }

    public async Task<IReadOnlyList<TemporaryPaymentPlan>>
        GetPaymentPlansAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var plans = await _database.Table<PaymentPlanRow>().ToListAsync();
        var installments = await _database
            .Table<PaymentInstallmentRow>()
            .ToListAsync();
        return plans.Select(row => new TemporaryPaymentPlan
        {
            Id = ParseKey(row.Id),
            Name = row.Name,
            Kind = Enum.IsDefined(typeof(PaymentPlanKind), row.Kind)
                ? (PaymentPlanKind)row.Kind
                : PaymentPlanKind.Temporary,
            OriginalAmount = row.OriginalAmount,
            TotalRepaymentAmount = row.TotalRepaymentAmount,
            Installments = installments
                .Where(x => x.PlanId == row.Id)
                .Select(FromRow)
                .OrderBy(x => x.DueDate)
                .ToArray()
        }).ToArray();
    }

    public async Task UpsertPaymentPlanAsync(
        TemporaryPaymentPlan plan,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            connection.InsertOrReplace(new PaymentPlanRow
            {
                Id = Key(plan.Id),
                Name = plan.Name,
                Kind = (int)plan.Kind,
                OriginalAmount = plan.OriginalAmount,
                TotalRepaymentAmount = plan.TotalRepaymentAmount
            });
            connection.Execute(
                "DELETE FROM payment_installments WHERE PlanId = ?",
                Key(plan.Id));
            foreach (var installment in plan.Installments)
            {
                connection.Insert(
                    ToRow(installment with { PlanId = plan.Id }));
            }
        });
    }

    public async Task DeletePaymentPlanAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM payment_installments WHERE PlanId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM payment_plans WHERE Id = ?",
            Key(id));
    }

    public async Task<IReadOnlyList<CreditCard>> GetCreditCardsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var cards = await _database.Table<CreditCardRow>().ToListAsync();
        var charges = await _database
            .Table<CardInstallmentRow>()
            .ToListAsync();
        var payments = await _database
            .Table<CreditCardPaymentPlanRow>()
            .ToListAsync();
        return cards.Select(row => FromRow(
            row,
            charges.Where(x => x.CreditCardId == row.Id),
            payments.Where(x => x.CreditCardId == row.Id))).ToArray();
    }

    public async Task UpsertCreditCardAsync(
        CreditCard card,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            connection.InsertOrReplace(ToRow(card));
            connection.Execute(
                "DELETE FROM card_installments WHERE CreditCardId = ?",
                Key(card.Id));
            foreach (var charge in card.Charges)
            {
                connection.Insert(
                    ToRow(charge with { CreditCardId = card.Id }));
            }

            connection.Execute(
                "DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?",
                Key(card.Id));
            foreach (var payment in card.PaymentPlans)
            {
                connection.Insert(
                    ToRow(payment with { CreditCardId = card.Id }));
            }
        });
    }

    public async Task DeleteCreditCardAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM card_installments WHERE CreditCardId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?",
            Key(id));
        await _database.ExecuteAsync(
            "DELETE FROM credit_cards WHERE Id = ?",
            Key(id));
    }

    public async Task<IReadOnlyList<PlannedLargeExpense>>
        GetPlannedLargeExpensesAsync(
            CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database
                .Table<PlannedLargeExpenseRow>()
                .ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.ExactDate)
            .ToArray();
    }

    public async Task UpsertPlannedLargeExpenseAsync(
        PlannedLargeExpense expense,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(expense));
    }

    public async Task DeletePlannedLargeExpenseAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync(
            "DELETE FROM planned_large_expenses WHERE Id = ?",
            Key(id));
    }

    public async Task ApplySimulationBatchAsync(
        SimulationPersistenceBatch batch,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await _database.RunInTransactionAsync(connection =>
        {
            foreach (var expense in batch.PlannedLargeExpenses)
            {
                connection.InsertOrReplace(ToRow(expense));
            }

            foreach (var plan in batch.PaymentPlans)
            {
                connection.InsertOrReplace(new PaymentPlanRow
                {
                    Id = Key(plan.Id),
                    Name = plan.Name,
                    Kind = (int)plan.Kind,
                    OriginalAmount = plan.OriginalAmount,
                    TotalRepaymentAmount = plan.TotalRepaymentAmount
                });
                connection.Execute(
                    "DELETE FROM payment_installments WHERE PlanId = ?",
                    Key(plan.Id));
                foreach (var installment in plan.Installments)
                {
                    connection.Insert(
                        ToRow(installment with { PlanId = plan.Id }));
                }
            }

            foreach (var card in batch.CreditCards)
            {
                connection.InsertOrReplace(ToRow(card));
                connection.Execute(
                    "DELETE FROM card_installments WHERE CreditCardId = ?",
                    Key(card.Id));
                foreach (var charge in card.Charges)
                {
                    connection.Insert(
                        ToRow(charge with { CreditCardId = card.Id }));
                }

                connection.Execute(
                    "DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?",
                    Key(card.Id));
                foreach (var payment in card.PaymentPlans)
                {
                    connection.Insert(
                        ToRow(payment with { CreditCardId = card.Id }));
                }
            }

            foreach (var income in batch.OtherIncomes)
            {
                connection.InsertOrReplace(ToRow(income));
            }

            foreach (var salary in batch.Salaries)
            {
                connection.InsertOrReplace(ToRow(salary));
            }

            foreach (var strategy in batch.PaymentAssignmentStrategies)
            {
                connection.InsertOrReplace(ToRow(strategy));
            }
        });
    }

    public async Task<FinancialHistoryData> GetFinancialHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var snapshotsTask = _database.Table<FinancialSnapshotRow>().ToListAsync();
        var plansTask = _database.Table<PeriodPlanSnapshotRow>().ToListAsync();
        var linesTask = _database.Table<PeriodPlanPaymentLineRow>().ToListAsync();
        var revisionsTask = _database.Table<PeriodPlanRevisionRow>().ToListAsync();
        var revisionLinesTask = _database
            .Table<PeriodPlanRevisionPaymentLineRow>()
            .ToListAsync();
        var actualsTask = _database.Table<PeriodActualRow>().ToListAsync();
        var paymentsTask = _database.Table<ActualPaymentRow>().ToListAsync();
        var flowsTask = _database.Table<ActualFlowRow>().ToListAsync();
        var breakdownTask = _database.Table<ActualLivingBreakdownRow>().ToListAsync();
        await Task.WhenAll(
            snapshotsTask, plansTask, linesTask, revisionsTask,
            revisionLinesTask, actualsTask, paymentsTask, flowsTask,
            breakdownTask);

        var lines = await linesTask;
        var revisionLines = await revisionLinesTask;
        var payments = await paymentsTask;
        var flows = await flowsTask;
        var breakdown = await breakdownTask;
        return new FinancialHistoryData(
            (await snapshotsTask).Select(FromRow).OrderBy(x => x.SnapshotDate).ThenBy(x => x.CreatedAtUtc).ToArray(),
            (await plansTask)
                .Select(row => FromRow(row, lines.Where(x => x.PeriodPlanSnapshotId == row.Id)))
                .OrderBy(x => x.PeriodStart)
                .ToArray(),
            (await revisionsTask)
                .Select(row => FromRow(
                    row,
                    revisionLines.Where(x =>
                        x.PeriodPlanRevisionId == row.Id)))
                .OrderBy(x => x.CreatedAtUtc)
                .ToArray(),
            (await actualsTask)
                .Select(row => FromRow(
                    row,
                    payments.Where(x => x.PeriodActualId == row.Id),
                    flows.Where(x => x.PeriodActualId == row.Id),
                    breakdown.Where(x => x.PeriodActualId == row.Id)))
                .OrderBy(x => x.PeriodStart)
                .ToArray());
    }

    public async Task SaveCurrentFinancialSnapshotAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        UserSettings? updatedSettings = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            connection.Execute("UPDATE financial_snapshots SET IsCurrent = 0 WHERE IsCurrent = 1");
            if (updatedSettings is not null)
            {
                UpdateSettings(connection, updatedSettings);
            }
            connection.Insert(ToRow(snapshot with { IsCurrent = true }));
            InsertPlan(connection, plan);
        });
    }

    public async Task ReplacePendingFinancialSnapshotPlanAsync(
        FinancialSnapshot snapshot,
        PeriodPlanSnapshot plan,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            var current = connection.Find<FinancialSnapshotRow>(
                Key(snapshot.Id));
            if (current is null || !current.IsCurrent)
            {
                throw new InvalidOperationException(
                    "Yalnızca güncel snapshot planı düzeltilebilir.");
            }

            var oldPlans = connection.Query<PeriodPlanSnapshotRow>(
                "SELECT * FROM period_plan_snapshots WHERE FinancialSnapshotId = ?",
                Key(snapshot.Id));
            if (oldPlans.Any(oldPlan =>
                    connection.FindWithQuery<PeriodActualRow>(
                        "SELECT * FROM period_actuals WHERE PeriodPlanSnapshotId = ? LIMIT 1",
                        oldPlan.Id) is not null))
            {
                throw new InvalidOperationException(
                    "Tamamlanmış dönem planı değiştirilemez.");
            }

            foreach (var oldPlan in oldPlans)
            {
                var oldRevisions = connection.Query<PeriodPlanRevisionRow>(
                    "SELECT * FROM period_plan_revisions WHERE PeriodPlanSnapshotId = ?",
                    oldPlan.Id);
                foreach (var oldRevision in oldRevisions)
                {
                    connection.Execute(
                        "DELETE FROM period_plan_revision_payment_lines WHERE PeriodPlanRevisionId = ?",
                        oldRevision.Id);
                    connection.Delete(oldRevision);
                }

                connection.Execute(
                    "DELETE FROM period_plan_payment_lines WHERE PeriodPlanSnapshotId = ?",
                    oldPlan.Id);
                connection.Delete(oldPlan);
            }

            connection.Update(ToRow(snapshot with { IsCurrent = true }));
            InsertPlan(connection, plan);
        });
    }

    public async Task SavePeriodPlanRevisionAsync(
        PeriodPlanRevision revision,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            var plan = connection.Find<PeriodPlanSnapshotRow>(
                Key(revision.PeriodPlanSnapshotId));
            if (plan is null)
            {
                throw new InvalidOperationException(
                    "Revize edilecek dönem planı bulunamadı.");
            }

            var finalized = connection.FindWithQuery<PeriodActualRow>(
                "SELECT * FROM period_actuals WHERE PeriodPlanSnapshotId = ? LIMIT 1",
                Key(revision.PeriodPlanSnapshotId));
            if (finalized is not null)
            {
                throw new InvalidOperationException(
                    "Tamamlanmış dönem planı revize edilemez.");
            }

            InsertRevision(connection, revision);
        });
    }

    public async Task FinalizeFinancialReviewAsync(
        FinancialReviewCommit commit,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await _database.RunInTransactionAsync(connection =>
        {
            var current = connection.FindWithQuery<FinancialSnapshotRow>(
                "SELECT * FROM financial_snapshots WHERE IsCurrent = 1 LIMIT 1");
            if (current is null ||
                current.Id != Key(commit.Actual.SourceFinancialSnapshotId))
            {
                throw new InvalidOperationException(
                    "Güncel finansal durum değişti. Dönemi yeniden açın.");
            }

            var existing = connection.FindWithQuery<PeriodActualRow>(
                "SELECT * FROM period_actuals WHERE PeriodPlanSnapshotId = ? LIMIT 1",
                Key(commit.Actual.PeriodPlanSnapshotId));
            if (existing is not null)
            {
                throw new InvalidOperationException("Bu dönem daha önce kaydedildi.");
            }

            if (commit.Revision is not null)
            {
                InsertRevision(connection, commit.Revision);
            }
            InsertActual(connection, commit.Actual);
            foreach (var loan in commit.UpdatedLoans)
            {
                connection.InsertOrReplace(ToRow(loan));
            }
            foreach (var plan in commit.UpdatedPaymentPlans)
            {
                InsertPaymentPlan(connection, plan);
            }
            foreach (var card in commit.UpdatedCreditCards)
            {
                InsertCreditCard(connection, card);
            }
            foreach (var expense in commit.UpdatedLargeExpenses)
            {
                connection.InsertOrReplace(ToRow(expense));
            }
            UpdateSettings(connection, commit.UpdatedSettings);
            connection.Execute("UPDATE financial_snapshots SET IsCurrent = 0 WHERE IsCurrent = 1");
            connection.Insert(ToRow(commit.NewSnapshot with { IsCurrent = true }));
            InsertPlan(connection, commit.NewPlan);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            await _database.CloseAsync();
            _initialized = false;
        }

        _initializeLock.Dispose();
    }

    internal static string FormatDate(DateOnly date) =>
        date.ToString(DateFormat, CultureInfo.InvariantCulture);

    internal static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseNullableDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseDate(value);

    private static string? FormatNullableDate(DateOnly? value) =>
        value is null ? null : FormatDate(value.Value);

    private static string Key(Guid id) => id.ToString("D");
    private static Guid ParseKey(string value) => Guid.Parse(value);
    private static string FormatInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static PaymentAssignmentStrategyRow ToRow(
        PaymentAssignmentStrategy value) => new()
        {
            Id = Key(value.Id),
            Mode = (int)value.Mode,
            EffectiveFromSalaryDate =
            FormatDate(value.EffectiveFromSalaryDate),
            CreatedAt = value.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            Note = value.Note
        };

    private static PaymentAssignmentStrategy FromRow(
        PaymentAssignmentStrategyRow row) => new()
        {
            Id = ParseKey(row.Id),
            Mode = (PaymentAssignmentMode)row.Mode,
            EffectiveFromSalaryDate =
            ParseDate(row.EffectiveFromSalaryDate),
            CreatedAt = DateTimeOffset.Parse(
            row.CreatedAt,
            CultureInfo.InvariantCulture),
            Note = row.Note
        };

    internal static SalaryRow ToRow(SalaryScheduleEntry value) => new()
    {
        Id = Key(value.Id),
        NetAmount = value.Amount,
        EffectiveFrom = FormatDate(value.EffectiveDate),
        Note = value.Description
    };

    private static SalaryScheduleEntry FromRow(SalaryRow row) => new()
    {
        Id = ParseKey(row.Id),
        Amount = row.NetAmount,
        EffectiveDate = ParseDate(row.EffectiveFrom),
        Description = row.Note
    };

    private static OtherIncomeRow ToRow(OneTimeIncome value) => new()
    {
        Id = Key(value.Id),
        Amount = value.Amount,
        ExactDate = FormatDate(value.ExactDate),
        Description = value.Description
    };

    private static OneTimeIncome FromRow(OtherIncomeRow row) => new()
    {
        Id = ParseKey(row.Id),
        Amount = row.Amount,
        ExactDate = ParseDate(row.ExactDate),
        Description = row.Description
    };

    internal static LoanRow ToRow(Loan value) => new()
    {
        Id = Key(value.Id),
        Name = value.Name,
        Bank = value.Bank,
        MonthlyInstallment = value.MonthlyPayment,
        PaymentDay = value.PaymentDay,
        StartDate = FormatDate(value.NextPaymentDate),
        EndDate = null,
        InstallmentCount = value.RemainingInstallmentCount,
        RemainingDebt = value.RemainingDebt,
        EarlyClosureAmount = value.EarlyClosureAmount,
        IsActive = value.IsActive
    };

    private static Loan FromRow(LoanRow row) => new()
    {
        Id = ParseKey(row.Id),
        Name = row.Name,
        Bank = row.Bank,
        MonthlyPayment = row.MonthlyInstallment,
        PaymentDay = row.PaymentDay,
        NextPaymentDate = ParseDate(row.StartDate),
        RemainingInstallmentCount = row.InstallmentCount.GetValueOrDefault(),
        RemainingDebt = row.RemainingDebt,
        EarlyClosureAmount = row.EarlyClosureAmount,
        IsActive = row.IsActive
    };

    internal static PaymentInstallmentRow ToRow(
        TemporaryPaymentInstallment value) => new()
        {
            Id = Key(value.Id),
            PlanId = Key(value.PlanId),
            DueDate = FormatDate(value.DueDate),
            Amount = value.Amount,
            IsPaid = value.IsPaid
        };

    private static TemporaryPaymentInstallment FromRow(
        PaymentInstallmentRow row) => new()
        {
            Id = ParseKey(row.Id),
            PlanId = ParseKey(row.PlanId),
            DueDate = ParseDate(row.DueDate),
            Amount = row.Amount,
            IsPaid = row.IsPaid
        };

    internal static CreditCardRow ToRow(CreditCard value) => new()
    {
        Id = Key(value.Id),
        Name = value.Name,
        Bank = value.Bank,
        Limit = value.Limit,
        CurrentTotalDebt = value.KnownTotalDebt,
        LastStatementDebt = value.CarriedBalance,
        LastStatementRemaining = value.CarriedBalance,
        CurrentCycleSpending = value.UnbilledSpending,
        StatementClosingDay = value.StatementClosingDay,
        PaymentDueDay = value.PaymentDueDay,
        MinimumPaymentRate = value.MinimumPaymentRate,
        PaymentMode = 0,
        ManualPaymentAmount = null,
        CarriedBalance = value.CarriedBalance,
        UnbilledSpending = value.UnbilledSpending,
        BalanceAsOfDate = FormatDate(value.BalanceAsOfDate),
        StatementModelVersion = CurrentCardStatementModelVersion,
        PaymentStrategy = (int)value.PaymentStrategy,
        FixedPaymentAmount = value.FixedPaymentAmount,
        ProjectionFallbackStrategy =
            (int)value.ProjectionFallbackStrategy,
        ProjectionFallbackFixedAmount =
            value.ProjectionFallbackFixedAmount
    };

    private static CreditCard FromRow(
        CreditCardRow row,
        IEnumerable<CardInstallmentRow> charges,
        IEnumerable<CreditCardPaymentPlanRow> paymentPlans) => new()
        {
            Id = ParseKey(row.Id),
            Name = row.Name,
            Bank = row.Bank,
            Limit = row.Limit,
            CarriedBalance = row.CarriedBalance,
            UnbilledSpending = row.UnbilledSpending,
            BalanceAsOfDate = ParseDate(row.BalanceAsOfDate),
            StatementClosingDay = row.StatementClosingDay,
            PaymentDueDay = row.PaymentDueDay,
            MinimumPaymentRate = row.MinimumPaymentRate,
            PaymentStrategy = (CreditCardPaymentStrategy)row.PaymentStrategy,
            FixedPaymentAmount = row.FixedPaymentAmount,
            ProjectionFallbackStrategy =
            (ProjectionFallbackStrategy)row.ProjectionFallbackStrategy,
            ProjectionFallbackFixedAmount =
            row.ProjectionFallbackFixedAmount,
            Charges = charges
            .Select(FromRow)
            .OrderBy(x => x.PostingDate)
            .ToArray(),
            PaymentPlans = paymentPlans
            .Select(FromRow)
            .OrderBy(x => x.DueDate)
            .ToArray()
        };

    internal static CardInstallmentRow ToRow(CardCharge value) => new()
    {
        Id = Key(value.Id),
        CreditCardId = Key(value.CreditCardId),
        Description = value.Description,
        DueDate = FormatDate(value.PostingDate),
        Amount = value.Amount
    };

    private static CardCharge FromRow(CardInstallmentRow row) => new()
    {
        Id = ParseKey(row.Id),
        CreditCardId = ParseKey(row.CreditCardId),
        Description = row.Description,
        PostingDate = ParseDate(row.DueDate),
        Amount = row.Amount
    };

    private static CreditCardPaymentPlanRow ToRow(
        CreditCardPaymentPlan value) => new()
        {
            Id = Key(value.Id),
            CreditCardId = Key(value.CreditCardId),
            DueDate = FormatDate(value.DueDate),
            PlannedPaymentAmount = value.Amount ?? 0m,
            PaymentType = (int)value.PaymentType,
            Amount = value.Amount
        };

    private static CreditCardPaymentPlan FromRow(
        CreditCardPaymentPlanRow row) => new()
        {
            Id = ParseKey(row.Id),
            CreditCardId = ParseKey(row.CreditCardId),
            DueDate = ParseDate(row.DueDate),
            PaymentType = (CreditCardPaymentType)row.PaymentType,
            Amount = row.Amount ??
                 (row.PlannedPaymentAmount > 0m
                     ? row.PlannedPaymentAmount
                     : null)
        };

    private static PlannedLargeExpenseRow ToRow(
        PlannedLargeExpense value) => new()
        {
            Id = Key(value.Id),
            Name = value.Name,
            Amount = value.Amount,
            ExactDate = FormatDate(value.ExactDate),
            Note = value.Note,
            Status = (int)value.Status
        };

    private static PlannedLargeExpense FromRow(
        PlannedLargeExpenseRow row) => new()
        {
            Id = ParseKey(row.Id),
            Name = row.Name,
            Amount = row.Amount,
            ExactDate = ParseDate(row.ExactDate),
            Note = row.Note,
            Status = (PlannedExpenseStatus)row.Status
        };

    private static FinancialSnapshotRow ToRow(FinancialSnapshot value) => new()
    {
        Id = Key(value.Id),
        SnapshotDate = FormatDate(value.SnapshotDate),
        ProjectionAnchorDate = FormatDate(value.ProjectionAnchorDate),
        NextReviewDate = FormatDate(value.NextReviewDate),
        ProjectionStartingSavings = value.ProjectionStartingSavings,
        SalaryDay = value.SalaryDay,
        PreviousSnapshotId = value.PreviousSnapshotId?.ToString("D"),
        Source = (int)value.Source,
        IsCurrent = value.IsCurrent,
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc),
        Note = value.Note
    };

    private static FinancialSnapshot FromRow(FinancialSnapshotRow row) => new()
    {
        Id = ParseKey(row.Id),
        SnapshotDate = ParseDate(row.SnapshotDate),
        ProjectionAnchorDate = ParseDate(row.ProjectionAnchorDate),
        NextReviewDate = ParseDate(row.NextReviewDate),
        ProjectionStartingSavings = row.ProjectionStartingSavings,
        SalaryDay = row.SalaryDay,
        PreviousSnapshotId = string.IsNullOrWhiteSpace(row.PreviousSnapshotId) ? null : ParseKey(row.PreviousSnapshotId),
        Source = (FinancialSnapshotSource)row.Source,
        IsCurrent = row.IsCurrent,
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc),
        Note = row.Note
    };

    private static PeriodPlanSnapshotRow ToRow(PeriodPlanSnapshot value) => new()
    {
        Id = Key(value.Id),
        FinancialSnapshotId = Key(value.FinancialSnapshotId),
        PeriodStart = FormatDate(value.PeriodStart),
        PeriodEnd = FormatDate(value.PeriodEnd),
        ReviewAvailableFrom = FormatDate(value.ReviewAvailableFrom),
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc),
        StrategyUsed = (int)value.StrategyUsed,
        PaymentWindowStart = FormatDate(value.PaymentWindowStart),
        PaymentWindowEnd = FormatDate(value.PaymentWindowEnd),
        OpeningSavings = value.OpeningSavings,
        PlannedIncome = value.PlannedIncome,
        PlannedLoanPayments = value.PlannedLoanPayments,
        PlannedCardPayments = value.PlannedCardPayments,
        PlannedTemporaryPayments = value.PlannedTemporaryPayments,
        PlannedInstallmentPayments = value.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = value.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = value.PlannedMandatoryPayments,
        PlannedLivingBudget = value.PlannedLivingBudget,
        PlannedLargeExpenses = value.PlannedLargeExpenses,
        PlannedCardInterest = value.PlannedCardInterest,
        PlannedDeficitInterest = value.PlannedDeficitInterest,
        PlannedEndingSavings = value.PlannedEndingSavings
    };

    private static PeriodPlanSnapshot FromRow(PeriodPlanSnapshotRow row, IEnumerable<PeriodPlanPaymentLineRow> lines) => new()
    {
        Id = ParseKey(row.Id),
        FinancialSnapshotId = ParseKey(row.FinancialSnapshotId),
        PeriodStart = ParseDate(row.PeriodStart),
        PeriodEnd = ParseDate(row.PeriodEnd),
        ReviewAvailableFrom = ParseDate(row.ReviewAvailableFrom),
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc),
        StrategyUsed = (PaymentAssignmentMode)row.StrategyUsed,
        PaymentWindowStart = ParseDate(row.PaymentWindowStart),
        PaymentWindowEnd = ParseDate(row.PaymentWindowEnd),
        OpeningSavings = row.OpeningSavings,
        PlannedIncome = row.PlannedIncome,
        PlannedLoanPayments = row.PlannedLoanPayments,
        PlannedCardPayments = row.PlannedCardPayments,
        PlannedTemporaryPayments = row.PlannedTemporaryPayments,
        PlannedInstallmentPayments = row.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = row.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = row.PlannedMandatoryPayments,
        PlannedLivingBudget = row.PlannedLivingBudget,
        PlannedLargeExpenses = row.PlannedLargeExpenses,
        PlannedCardInterest = row.PlannedCardInterest,
        PlannedDeficitInterest = row.PlannedDeficitInterest,
        PlannedEndingSavings = row.PlannedEndingSavings,
        PaymentLines = lines.Select(FromRow).OrderBy(x => x.PlannedDate).ThenBy(x => x.Name).ToArray()
    };

    private static PeriodPlanPaymentLineRow ToRow(PeriodPlanPaymentLine value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanSnapshotId = Key(value.PeriodPlanSnapshotId),
        SourceEntityId = Key(value.SourceEntityId),
        SourceType = (int)value.SourceType,
        Name = value.Name,
        PlannedDate = FormatDate(value.PlannedDate),
        PlannedAmount = value.PlannedAmount,
        IsEstimate = value.IsEstimate,
        Detail = value.Detail
    };

    private static PeriodPlanPaymentLine FromRow(PeriodPlanPaymentLineRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = ParseKey(row.PeriodPlanSnapshotId),
        SourceEntityId = ParseKey(row.SourceEntityId),
        SourceType = (PlanPaymentSourceType)row.SourceType,
        Name = row.Name,
        PlannedDate = ParseDate(row.PlannedDate),
        PlannedAmount = row.PlannedAmount,
        IsEstimate = row.IsEstimate,
        Detail = row.Detail
    };

    private static PeriodPlanRevisionRow ToRow(PeriodPlanRevision value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanSnapshotId = Key(value.PeriodPlanSnapshotId),
        RevisionNumber = value.RevisionNumber,
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc),
        Trigger = value.Trigger,
        StrategyUsed = (int)value.StrategyUsed,
        PlannedIncome = value.PlannedIncome,
        PlannedLoanPayments = value.PlannedLoanPayments,
        PlannedCardPayments = value.PlannedCardPayments,
        PlannedTemporaryPayments = value.PlannedTemporaryPayments,
        PlannedInstallmentPayments = value.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = value.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = value.PlannedMandatoryPayments,
        PlannedLivingBudget = value.PlannedLivingBudget,
        PlannedLargeExpenses = value.PlannedLargeExpenses,
        PlannedCardInterest = value.PlannedCardInterest,
        PlannedDeficitInterest = value.PlannedDeficitInterest,
        PlannedInterest = value.PlannedInterest,
        PlannedEndingSavings = value.PlannedEndingSavings,
        Note = value.Note
    };

    private static PeriodPlanRevision FromRow(
        PeriodPlanRevisionRow row,
        IEnumerable<PeriodPlanRevisionPaymentLineRow> lines) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = ParseKey(row.PeriodPlanSnapshotId),
        RevisionNumber = row.RevisionNumber,
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc),
        Trigger = row.Trigger,
        StrategyUsed = (PaymentAssignmentMode)row.StrategyUsed,
        PlannedIncome = row.PlannedIncome,
        PlannedLoanPayments = row.PlannedLoanPayments,
        PlannedCardPayments = row.PlannedCardPayments,
        PlannedTemporaryPayments = row.PlannedTemporaryPayments,
        PlannedInstallmentPayments = row.PlannedInstallmentPayments,
        PlannedOtherScheduledPayments = row.PlannedOtherScheduledPayments,
        PlannedMandatoryPayments = row.PlannedMandatoryPayments,
        PlannedLivingBudget = row.PlannedLivingBudget,
        PlannedLargeExpenses = row.PlannedLargeExpenses,
        PlannedCardInterest = row.PlannedCardInterest,
        PlannedDeficitInterest = row.PlannedDeficitInterest,
        PlannedInterest = row.PlannedInterest,
        PlannedEndingSavings = row.PlannedEndingSavings,
        Note = row.Note,
        PaymentLines = lines
            .Select(line => FromRow(line, ParseKey(row.PeriodPlanSnapshotId)))
            .OrderBy(x => x.PlannedDate)
            .ThenBy(x => x.Name)
            .ToArray()
    };

    private static PeriodPlanRevisionPaymentLineRow ToRevisionRow(
        Guid revisionId,
        PeriodPlanPaymentLine value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanRevisionId = Key(revisionId),
        SourceEntityId = Key(value.SourceEntityId),
        SourceType = (int)value.SourceType,
        Name = value.Name,
        PlannedDate = FormatDate(value.PlannedDate),
        PlannedAmount = value.PlannedAmount,
        IsEstimate = value.IsEstimate,
        Detail = value.Detail
    };

    private static PeriodPlanPaymentLine FromRow(
        PeriodPlanRevisionPaymentLineRow row,
        Guid periodPlanSnapshotId) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = periodPlanSnapshotId,
        SourceEntityId = ParseKey(row.SourceEntityId),
        SourceType = (PlanPaymentSourceType)row.SourceType,
        Name = row.Name,
        PlannedDate = ParseDate(row.PlannedDate),
        PlannedAmount = row.PlannedAmount,
        IsEstimate = row.IsEstimate,
        Detail = row.Detail
    };

    private static PeriodActualRow ToRow(PeriodActual value) => new()
    {
        Id = Key(value.Id),
        PeriodPlanSnapshotId = Key(value.PeriodPlanSnapshotId),
        SourceFinancialSnapshotId = Key(value.SourceFinancialSnapshotId),
        ResultFinancialSnapshotId = Key(value.ResultFinancialSnapshotId),
        PeriodStart = FormatDate(value.PeriodStart),
        PeriodEnd = FormatDate(value.PeriodEnd),
        FinalizedAtUtc = FormatInstant(value.FinalizedAtUtc),
        ActualIncome = value.ActualIncome,
        ActualLoanPayments = value.ActualLoanPayments,
        ActualCardPayments = value.ActualCardPayments,
        ActualTemporaryPayments = value.ActualTemporaryPayments,
        ActualInstallmentPayments = value.ActualInstallmentPayments,
        ActualOtherScheduledPayments = value.ActualOtherScheduledPayments,
        ActualLargeExpenses = value.ActualLargeExpenses,
        ActualMandatoryPayments = value.ActualMandatoryPayments,
        ActualLivingSpend = value.ActualLivingSpend,
        ActualInterest = value.ActualInterest,
        UnplannedIncome = value.UnplannedIncome,
        UnplannedPayments = value.UnplannedPayments,
        DerivedEndingSavings = value.DerivedEndingSavings,
        ConfirmedEndingSavings = value.ConfirmedEndingSavings,
        ReconciliationAdjustment = value.ReconciliationAdjustment,
        ComparisonSummary = value.ComparisonSummary,
        Note = value.Note
    };

    private static PeriodActual FromRow(PeriodActualRow row, IEnumerable<ActualPaymentRow> payments, IEnumerable<ActualFlowRow> flows, IEnumerable<ActualLivingBreakdownRow> breakdown) => new()
    {
        Id = ParseKey(row.Id),
        PeriodPlanSnapshotId = ParseKey(row.PeriodPlanSnapshotId),
        SourceFinancialSnapshotId = ParseKey(row.SourceFinancialSnapshotId),
        ResultFinancialSnapshotId = ParseKey(row.ResultFinancialSnapshotId),
        PeriodStart = ParseDate(row.PeriodStart),
        PeriodEnd = ParseDate(row.PeriodEnd),
        FinalizedAtUtc = ParseInstant(row.FinalizedAtUtc),
        ActualIncome = row.ActualIncome,
        ActualLoanPayments = row.ActualLoanPayments,
        ActualCardPayments = row.ActualCardPayments,
        ActualTemporaryPayments = row.ActualTemporaryPayments,
        ActualInstallmentPayments = row.ActualInstallmentPayments,
        ActualOtherScheduledPayments = row.ActualOtherScheduledPayments,
        ActualLargeExpenses = row.ActualLargeExpenses,
        ActualMandatoryPayments = row.ActualMandatoryPayments,
        ActualLivingSpend = row.ActualLivingSpend,
        ActualInterest = row.ActualInterest,
        UnplannedIncome = row.UnplannedIncome,
        UnplannedPayments = row.UnplannedPayments,
        DerivedEndingSavings = row.DerivedEndingSavings,
        ConfirmedEndingSavings = row.ConfirmedEndingSavings,
        ReconciliationAdjustment = row.ReconciliationAdjustment,
        ComparisonSummary = row.ComparisonSummary,
        Note = row.Note,
        Payments = payments.Select(FromRow).OrderBy(x => x.PlannedDate).ToArray(),
        Flows = flows.Select(FromRow).OrderBy(x => x.Date).ToArray(),
        LivingBreakdown = breakdown.Select(FromRow).OrderBy(x => x.Category).ToArray()
    };

    private static ActualPaymentRow ToRow(ActualPayment value) => new()
    {
        Id = Key(value.Id),
        PeriodActualId = Key(value.PeriodActualId),
        PeriodPlanPaymentLineId = Key(value.PeriodPlanPaymentLineId),
        SourceEntityId = Key(value.SourceEntityId),
        SourceType = (int)value.SourceType,
        Name = value.Name,
        PlannedDate = FormatDate(value.PlannedDate),
        PlannedAmount = value.PlannedAmount,
        ActualPaymentDate = FormatNullableDate(value.ActualPaymentDate),
        ActualAmount = value.ActualAmount,
        Status = (int)value.Status,
        Note = value.Note
    };

    private static ActualPayment FromRow(ActualPaymentRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodActualId = ParseKey(row.PeriodActualId),
        PeriodPlanPaymentLineId = ParseKey(row.PeriodPlanPaymentLineId),
        SourceEntityId = ParseKey(row.SourceEntityId),
        SourceType = (PlanPaymentSourceType)row.SourceType,
        Name = row.Name,
        PlannedDate = ParseDate(row.PlannedDate),
        PlannedAmount = row.PlannedAmount,
        ActualPaymentDate = ParseNullableDate(row.ActualPaymentDate),
        ActualAmount = row.ActualAmount,
        Status = (ActualPaymentStatus)row.Status,
        Note = row.Note
    };

    private static ActualFlowRow ToRow(ActualFlow value) => new()
    {
        Id = Key(value.Id),
        PeriodActualId = Key(value.PeriodActualId),
        Type = (int)value.Type,
        Name = value.Name,
        Category = value.Category,
        Date = FormatDate(value.Date),
        Amount = value.Amount
    };

    private static ActualFlow FromRow(ActualFlowRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodActualId = ParseKey(row.PeriodActualId),
        Type = (ActualFlowType)row.Type,
        Name = row.Name,
        Category = row.Category,
        Date = ParseDate(row.Date),
        Amount = row.Amount
    };

    private static ActualLivingBreakdownRow ToRow(ActualLivingBreakdown value) => new()
    {
        Id = Key(value.Id),
        PeriodActualId = Key(value.PeriodActualId),
        Category = value.Category,
        Amount = value.Amount
    };

    private static ActualLivingBreakdown FromRow(ActualLivingBreakdownRow row) => new()
    {
        Id = ParseKey(row.Id),
        PeriodActualId = ParseKey(row.PeriodActualId),
        Category = row.Category,
        Amount = row.Amount
    };

    private static void InsertPlan(SQLiteConnection connection, PeriodPlanSnapshot plan)
    {
        connection.Insert(ToRow(plan));
        foreach (var line in plan.PaymentLines)
        {
            connection.Insert(ToRow(line with { PeriodPlanSnapshotId = plan.Id }));
        }
    }

    private static void InsertActual(SQLiteConnection connection, PeriodActual actual)
    {
        connection.Insert(ToRow(actual));
        foreach (var payment in actual.Payments) connection.Insert(ToRow(payment with { PeriodActualId = actual.Id }));
        foreach (var flow in actual.Flows) connection.Insert(ToRow(flow with { PeriodActualId = actual.Id }));
        foreach (var item in actual.LivingBreakdown) connection.Insert(ToRow(item with { PeriodActualId = actual.Id }));
    }

    private static void InsertRevision(
        SQLiteConnection connection,
        PeriodPlanRevision revision)
    {
        connection.Insert(ToRow(revision));
        foreach (var line in revision.PaymentLines)
        {
            connection.Insert(ToRevisionRow(
                revision.Id,
                line with
                {
                    PeriodPlanSnapshotId = revision.PeriodPlanSnapshotId
                }));
        }
    }

    private static void InsertPaymentPlan(SQLiteConnection connection, TemporaryPaymentPlan plan)
    {
        connection.InsertOrReplace(new PaymentPlanRow
        {
            Id = Key(plan.Id),
            Name = plan.Name,
            Kind = (int)plan.Kind,
            OriginalAmount = plan.OriginalAmount,
            TotalRepaymentAmount = plan.TotalRepaymentAmount
        });
        connection.Execute("DELETE FROM payment_installments WHERE PlanId = ?", Key(plan.Id));
        foreach (var installment in plan.Installments)
        {
            connection.Insert(ToRow(installment with { PlanId = plan.Id }));
        }
    }

    private static void InsertCreditCard(SQLiteConnection connection, CreditCard card)
    {
        connection.InsertOrReplace(ToRow(card));
        connection.Execute("DELETE FROM card_installments WHERE CreditCardId = ?", Key(card.Id));
        foreach (var charge in card.Charges) connection.Insert(ToRow(charge with { CreditCardId = card.Id }));
        connection.Execute("DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?", Key(card.Id));
        foreach (var payment in card.PaymentPlans) connection.Insert(ToRow(payment with { CreditCardId = card.Id }));
    }

    private static void UpdateSettings(SQLiteConnection connection, UserSettings settings)
    {
        var row = connection.Table<SettingsRow>().First();
        row.SalaryDay = settings.SalaryDay; row.MonthlyLivingBudget = settings.MonthlyLivingBudget;
        row.ProjectionStartingSavings = settings.ProjectionStartingSavings;
        row.ProjectionAnchorDate = settings.ProjectionAnchorDate == default ? null : FormatDate(settings.ProjectionAnchorDate);
        row.CreditCardCarryInterestRate = settings.CreditCardCarryInterestRate;
        row.DeficitFinancingInterestRate = settings.DeficitFinancingInterestRate;
        row.SchemaVersion = CurrentSchemaVersion;
        connection.Update(row);
    }

    private SettingsRow DefaultSettingsRow() => new()
    {
        SalaryDay = 10,
        MonthlyLivingBudget = 0m,
        ProjectionStartingSavings = 0m,
        ProjectionAnchorDate = null,
        CreditCardCarryInterestRate = DefaultPlanningInterestRate,
        DeficitFinancingInterestRate = DefaultPlanningInterestRate,
        PaymentAssignmentMode =
            (int)PaymentAssignmentMode.UpcomingPeriod,
        SchemaVersion = CurrentSchemaVersion,
        DevelopmentSeedVersion = 0,
        DevelopmentSeedEnabled = _developmentFeaturesEnabled,
        LegacyRemovedFeatureFlag = false,
        TrackingStartedDate = null
    };

    private async Task EnsureInitialPaymentAssignmentStrategyAsync(
        SettingsRow settings)
    {
        if (await _database
                .Table<PaymentAssignmentStrategyRow>()
                .CountAsync() > 0)
        {
            return;
        }

        var anchor = ParseDate(
            settings.ProjectionAnchorDate ?? FormatDate(_migrationDate));
        var firstSalary = new SalaryPeriodCalculator()
            .GetFirstSalaryOnOrAfter(anchor, settings.SalaryDay);
        var legacyMode = Enum.IsDefined(
            typeof(PaymentAssignmentMode),
            settings.PaymentAssignmentMode)
            ? (PaymentAssignmentMode)settings.PaymentAssignmentMode
            : PaymentAssignmentMode.UpcomingPeriod;
        await _database.InsertAsync(ToRow(
            new PaymentAssignmentStrategy
            {
                Id = LegacyInitialAssignmentStrategyId,
                Mode = legacyMode,
                EffectiveFromSalaryDate = firstSalary,
                CreatedAt = new DateTimeOffset(
                    _migrationDate.ToDateTime(TimeOnly.MinValue),
                    TimeSpan.Zero),
                Note = "İlk maaş kullanım düzeni"
            }));
    }

    private async Task RemoveObsoleteDailyTrackingTablesAsync()
    {
        await _database.ExecuteAsync("DROP TABLE IF EXISTS expenses");
        await _database.ExecuteAsync(
            "DROP TABLE IF EXISTS spendable_balance_snapshots");
        await _database.ExecuteAsync("DROP TABLE IF EXISTS emergency_fund");
        await _database.ExecuteAsync(
            "DROP TABLE IF EXISTS emergency_fund_transfers");
    }

    private async Task MigratePeriodPlanRevisionSchemaAsync()
    {
        await EnsureColumnAsync(
            "period_plan_revisions",
            "RevisionNumber",
            "RevisionNumber INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "Trigger",
            "Trigger TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "StrategyUsed",
            "StrategyUsed INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedLoanPayments",
            "PlannedLoanPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedCardPayments",
            "PlannedCardPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedTemporaryPayments",
            "PlannedTemporaryPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedInstallmentPayments",
            "PlannedInstallmentPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedOtherScheduledPayments",
            "PlannedOtherScheduledPayments decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedCardInterest",
            "PlannedCardInterest decimal NOT NULL DEFAULT 0");
        await EnsureColumnAsync(
            "period_plan_revisions",
            "PlannedDeficitInterest",
            "PlannedDeficitInterest decimal NOT NULL DEFAULT 0");
        await BackfillLegacyPeriodPlanRevisionSummariesAsync();
    }

    private async Task BackfillLegacyPeriodPlanRevisionSummariesAsync()
    {
        await _database.ExecuteAsync(
            """
            UPDATE period_plan_revisions
            SET
                RevisionNumber = CASE
                    WHEN RevisionNumber = 0 THEN (
                        SELECT COUNT(*)
                        FROM period_plan_revisions prior
                        WHERE prior.PeriodPlanSnapshotId = period_plan_revisions.PeriodPlanSnapshotId
                          AND prior.CreatedAtUtc <= period_plan_revisions.CreatedAtUtc)
                    ELSE RevisionNumber
                END,
                Trigger = CASE
                    WHEN Trigger = '' THEN Note
                    ELSE Trigger
                END,
                StrategyUsed = COALESCE((
                    SELECT StrategyUsed
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), StrategyUsed),
                PlannedLoanPayments = COALESCE((
                    SELECT PlannedLoanPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedLoanPayments),
                PlannedCardPayments = COALESCE((
                    SELECT PlannedCardPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedCardPayments),
                PlannedTemporaryPayments = COALESCE((
                    SELECT PlannedTemporaryPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedTemporaryPayments),
                PlannedInstallmentPayments = COALESCE((
                    SELECT PlannedInstallmentPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedInstallmentPayments),
                PlannedOtherScheduledPayments = COALESCE((
                    SELECT PlannedOtherScheduledPayments
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedOtherScheduledPayments),
                PlannedCardInterest = COALESCE((
                    SELECT PlannedCardInterest
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), PlannedCardInterest),
                PlannedDeficitInterest = PlannedInterest - COALESCE((
                    SELECT PlannedCardInterest
                    FROM period_plan_snapshots
                    WHERE Id = period_plan_revisions.PeriodPlanSnapshotId), 0)
            WHERE NOT EXISTS (
                SELECT 1
                FROM period_plan_revision_payment_lines
                WHERE PeriodPlanRevisionId = period_plan_revisions.Id)
            """);
    }

    private async Task EnsureColumnAsync(
        string table,
        string column,
        string definition)
    {
        var columns = await _database.QueryAsync<TableInfoRow>(
            $"PRAGMA table_info({table})");
        if (columns.Any(x => string.Equals(
                x.Name,
                column,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await _database.ExecuteAsync(
            $"ALTER TABLE {table} ADD COLUMN {definition}");
    }

    private async Task MigrateLegacyCreditCardsAsync()
    {
        var cards = await _database.Table<CreditCardRow>().ToListAsync();
        foreach (var row in cards.Where(x =>
                     x.StatementModelVersion <
                     CurrentCardStatementModelVersion))
        {
            if (row.StatementModelVersion < 2)
            {
                row.CarriedBalance = row.LastStatementRemaining > 0m
                    ? row.LastStatementRemaining
                    : row.LastStatementDebt;
                row.UnbilledSpending = row.CurrentCycleSpending;
                row.BalanceAsOfDate = FormatDate(_migrationDate);

                if (row.PaymentMode == 1 &&
                    row.ManualPaymentAmount is > 0m)
                {
                    var close = CreditCardStatementCalculator
                        .ResolveStatementCloseOnOrAfter(
                            _migrationDate,
                            row.StatementClosingDay);
                    var due = CreditCardStatementCalculator
                        .ResolvePaymentDueDate(
                            close,
                            row.PaymentDueDay);
                    await _database.InsertOrReplaceAsync(ToRow(
                        new CreditCardPaymentPlan
                        {
                            CreditCardId = ParseKey(row.Id),
                            DueDate = due,
                            PaymentType =
                                CreditCardPaymentType.FixedAmount,
                            Amount = row.ManualPaymentAmount.Value
                        }));
                }
            }

            if (row.StatementModelVersion < 3)
            {
                row.PaymentStrategy =
                    (int)CreditCardPaymentStrategy.AskEachStatement;
                row.FixedPaymentAmount = null;
                row.ProjectionFallbackStrategy =
                    (int)ProjectionFallbackStrategy.None;
                row.ProjectionFallbackFixedAmount = null;
            }

            if (string.IsNullOrWhiteSpace(row.BalanceAsOfDate))
            {
                row.BalanceAsOfDate = FormatDate(_migrationDate);
            }

            row.StatementModelVersion =
                CurrentCardStatementModelVersion;
            await _database.UpdateAsync(row);
        }
    }

    private sealed class TableInfoRow
    {
        [Column("name")]
        public string Name { get; set; } = string.Empty;
    }
}

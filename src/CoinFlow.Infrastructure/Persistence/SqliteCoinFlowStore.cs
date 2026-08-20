using System.Globalization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

public sealed class SqliteCoinFlowStore : ICoinFlowStore, IAsyncDisposable
{
    private const string DateFormat = "yyyy-MM-dd";
    private const int CurrentSchemaVersion = 6;
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
                "Canonical seed yalnızca development build'de yüklenebilir.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        await DevelopmentDataSeeder.SeedAsync(_database);
        var settings = await _database.Table<SettingsRow>().FirstAsync();
        settings.SalaryDay = 10;
        settings.MonthlyLivingBudget = 30_000m;
        settings.ProjectionStartingSavings = 0m;
        settings.ProjectionAnchorDate = FormatDate(
            new DateOnly(2026, 8, 20));
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
        await _database.InsertOrReplaceAsync(new PaymentPlanRow
        {
            Id = Key(plan.Id),
            Name = plan.Name,
            Kind = (int)plan.Kind
        });
        await _database.ExecuteAsync(
            "DELETE FROM payment_installments WHERE PlanId = ?",
            Key(plan.Id));
        foreach (var installment in plan.Installments)
        {
            await _database.InsertAsync(
                ToRow(installment with { PlanId = plan.Id }));
        }
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
        await _database.InsertOrReplaceAsync(ToRow(card));
        await _database.ExecuteAsync(
            "DELETE FROM card_installments WHERE CreditCardId = ?",
            Key(card.Id));
        foreach (var charge in card.Charges)
        {
            await _database.InsertAsync(
                ToRow(charge with { CreditCardId = card.Id }));
        }

        await _database.ExecuteAsync(
            "DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?",
            Key(card.Id));
        foreach (var payment in card.PaymentPlans)
        {
            await _database.InsertAsync(
                ToRow(payment with { CreditCardId = card.Id }));
        }
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
        StatementModelVersion = CurrentSchemaVersion,
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

    private SettingsRow DefaultSettingsRow() => new()
    {
        SalaryDay = 10,
        MonthlyLivingBudget = 0m,
        ProjectionStartingSavings = 0m,
        ProjectionAnchorDate = null,
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

    private async Task MigrateLegacyCreditCardsAsync()
    {
        var cards = await _database.Table<CreditCardRow>().ToListAsync();
        foreach (var row in cards.Where(x =>
                     x.StatementModelVersion < CurrentSchemaVersion))
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

            row.StatementModelVersion = CurrentSchemaVersion;
            await _database.UpdateAsync(row);
        }
    }
}

using System.Globalization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

public sealed class SqliteCoinFlowStore : ICoinFlowStore, IAsyncDisposable
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly SQLiteAsyncConnection _database;
    private readonly bool _seedDevelopmentData;
    private readonly DateOnly _migrationDate;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public SqliteCoinFlowStore(string databasePath, bool seedDevelopmentData, DateOnly migrationDate)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Veritabanı yolu gereklidir.", nameof(databasePath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        SQLitePCL.Batteries_V2.Init();
        _database = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
        _seedDevelopmentData = seedDevelopmentData;
        _migrationDate = migrationDate;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
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
            await _database.CreateTableAsync<LoanRow>();
            await _database.CreateTableAsync<PaymentPlanRow>();
            await _database.CreateTableAsync<PaymentInstallmentRow>();
            await _database.CreateTableAsync<CreditCardRow>();
            await _database.CreateTableAsync<CardInstallmentRow>();
            await _database.CreateTableAsync<CreditCardPaymentPlanRow>();
            await _database.CreateTableAsync<ExpenseRow>();
            await _database.CreateTableAsync<SpendableBalanceSnapshotRow>();
            await _database.CreateTableAsync<SettingsRow>();
            await _database.CreateTableAsync<EmergencyFundRow>();
            await _database.CreateTableAsync<EmergencyFundTransferRow>();

            await MigrateLegacyCreditCardsAsync();

            if (await _database.Table<SettingsRow>().CountAsync() == 0)
            {
                await _database.InsertAsync(new SettingsRow
                {
                    SalaryDay = 10,
                    GamificationEnabled = true,
                    DevelopmentSeedEnabled = _seedDevelopmentData,
                    TrackingStartedDate = FormatDate(_migrationDate)
                });

                await _database.InsertAsync(ToRow(new EmergencyFund
                {
                    TargetAmount = 150_000m,
                    CurrentAmount = 32_000m
                }));

                if (_seedDevelopmentData)
                {
                    await DevelopmentDataSeeder.SeedAsync(_database);
                }
            }

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task ResetAllDataAsync(CancellationToken cancellationToken = default)
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
            connection.DeleteAll<ExpenseRow>();
            connection.DeleteAll<SpendableBalanceSnapshotRow>();
            connection.DeleteAll<SalaryRow>();
            connection.DeleteAll<LoanRow>();
            connection.DeleteAll<EmergencyFundTransferRow>();
            connection.DeleteAll<EmergencyFundRow>();
            connection.DeleteAll<SettingsRow>();

            connection.Insert(new SettingsRow
            {
                SalaryDay = 10,
                GamificationEnabled = true,
                DevelopmentSeedEnabled = false,
                TrackingStartedDate = FormatDate(_migrationDate)
            });
            connection.Insert(ToRow(new EmergencyFund()));
        });
    }

    public async Task<UserSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<SettingsRow>().FirstAsync();
        return new UserSettings
        {
            SalaryDay = row.SalaryDay,
            GamificationEnabled = row.GamificationEnabled,
            DevelopmentSeedEnabled = row.DevelopmentSeedEnabled,
            TrackingStartedDate = ParseNullableDate(row.TrackingStartedDate)
        };
    }

    public async Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(new SettingsRow
        {
            SalaryDay = settings.SalaryDay,
            GamificationEnabled = settings.GamificationEnabled,
            DevelopmentSeedEnabled = settings.DevelopmentSeedEnabled,
            TrackingStartedDate = FormatNullableDate(settings.TrackingStartedDate)
        });
    }

    public async Task<IReadOnlyList<SalaryScheduleEntry>> GetSalaryScheduleAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<SalaryRow>().ToListAsync()).Select(FromRow).OrderBy(x => x.EffectiveFrom).ToArray();
    }

    public async Task UpsertSalaryAsync(SalaryScheduleEntry entry, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(entry));
    }

    public async Task DeleteSalaryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync("DELETE FROM salary_schedule WHERE Id = ?", Key(id));
    }

    public async Task<IReadOnlyList<Loan>> GetLoansAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<LoanRow>().ToListAsync()).Select(FromRow).OrderBy(x => x.PaymentDay).ToArray();
    }

    public async Task UpsertLoanAsync(Loan loan, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(loan));
    }

    public async Task DeleteLoanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync("DELETE FROM loans WHERE Id = ?", Key(id));
    }

    public async Task<IReadOnlyList<TemporaryPaymentPlan>> GetPaymentPlansAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var plans = await _database.Table<PaymentPlanRow>().ToListAsync();
        var installments = await _database.Table<PaymentInstallmentRow>().ToListAsync();
        return plans.Select(row => new TemporaryPaymentPlan
        {
            Id = ParseKey(row.Id),
            Name = row.Name,
            Kind = (PaymentPlanKind)row.Kind,
            Installments = installments
                .Where(x => x.PlanId == row.Id)
                .Select(FromRow)
                .OrderBy(x => x.DueDate)
                .ToArray()
        }).ToArray();
    }

    public async Task UpsertPaymentPlanAsync(TemporaryPaymentPlan plan, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(new PaymentPlanRow
        {
            Id = Key(plan.Id),
            Name = plan.Name,
            Kind = (int)plan.Kind
        });
        await _database.ExecuteAsync("DELETE FROM payment_installments WHERE PlanId = ?", Key(plan.Id));
        foreach (var installment in plan.Installments)
        {
            await _database.InsertAsync(ToRow(installment with { PlanId = plan.Id }));
        }
    }

    public async Task DeletePaymentPlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync("DELETE FROM payment_installments WHERE PlanId = ?", Key(id));
        await _database.ExecuteAsync("DELETE FROM payment_plans WHERE Id = ?", Key(id));
    }

    public async Task<IReadOnlyList<CreditCard>> GetCreditCardsAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var cards = await _database.Table<CreditCardRow>().ToListAsync();
        var installments = await _database.Table<CardInstallmentRow>().ToListAsync();
        var payments = await _database.Table<CreditCardPaymentPlanRow>().ToListAsync();
        return cards.Select(row => FromRow(
            row,
            installments.Where(x => x.CreditCardId == row.Id),
            payments.Where(x => x.CreditCardId == row.Id))).ToArray();
    }

    public async Task UpsertCreditCardAsync(CreditCard card, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(card));
        await _database.ExecuteAsync("DELETE FROM card_installments WHERE CreditCardId = ?", Key(card.Id));
        foreach (var charge in card.Charges)
        {
            await _database.InsertAsync(ToRow(charge with { CreditCardId = card.Id }));
        }
        await _database.ExecuteAsync("DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?", Key(card.Id));
        foreach (var payment in card.PaymentPlans)
        {
            await _database.InsertAsync(ToRow(payment with { CreditCardId = card.Id }));
        }
    }

    public async Task DeleteCreditCardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync("DELETE FROM card_installments WHERE CreditCardId = ?", Key(id));
        await _database.ExecuteAsync("DELETE FROM credit_card_payment_plans WHERE CreditCardId = ?", Key(id));
        await _database.ExecuteAsync("DELETE FROM credit_cards WHERE Id = ?", Key(id));
    }

    public async Task<IReadOnlyList<Expense>> GetExpensesAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<ExpenseRow>().ToListAsync())
            .Select(FromRow)
            .Where(x => from is null || x.Date >= from.Value)
            .Where(x => to is null || x.Date <= to.Value)
            .OrderByDescending(x => x.Date)
            .ToArray();
    }

    public async Task UpsertExpenseAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(expense));
    }

    public async Task DeleteExpenseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync("DELETE FROM expenses WHERE Id = ?", Key(id));
    }

    public async Task<IReadOnlyList<SpendableBalanceSnapshot>> GetSpendableBalanceSnapshotsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<SpendableBalanceSnapshotRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.SnapshotDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ToArray();
    }

    public async Task UpsertSpendableBalanceSnapshotAsync(
        SpendableBalanceSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(snapshot));
    }

    public async Task<EmergencyFund> GetEmergencyFundAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var row = await _database.Table<EmergencyFundRow>().FirstAsync();
        return FromRow(row);
    }

    public async Task SaveEmergencyFundAsync(EmergencyFund emergencyFund, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var existing = await _database.Table<EmergencyFundRow>().FirstOrDefaultAsync();
        var value = emergencyFund with { Id = existing is null ? emergencyFund.Id : ParseKey(existing.Id) };
        await _database.InsertOrReplaceAsync(ToRow(value));
    }

    public async Task<IReadOnlyList<EmergencyFundTransfer>> GetEmergencyFundTransfersAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        return (await _database.Table<EmergencyFundTransferRow>().ToListAsync())
            .Select(FromRow)
            .OrderBy(x => x.TransferDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ToArray();
    }

    public async Task UpsertEmergencyFundTransferAsync(
        EmergencyFundTransfer transfer,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(transfer));
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

    internal static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
    internal static DateOnly ParseDate(string value) => DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
    internal static string FormatInstant(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    internal static DateTimeOffset ParseInstant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateOnly? ParseNullableDate(string? value) => string.IsNullOrWhiteSpace(value) ? null : ParseDate(value);
    private static string? FormatNullableDate(DateOnly? value) => value is null ? null : FormatDate(value.Value);
    private static string Key(Guid id) => id.ToString("D");
    private static Guid ParseKey(string value) => Guid.Parse(value);

    internal static SalaryRow ToRow(SalaryScheduleEntry value) => new()
    {
        Id = Key(value.Id),
        NetAmount = value.NetAmount,
        EffectiveFrom = FormatDate(value.EffectiveFrom),
        Note = value.Note
    };

    private static SalaryScheduleEntry FromRow(SalaryRow row) => new()
    {
        Id = ParseKey(row.Id),
        NetAmount = row.NetAmount,
        EffectiveFrom = ParseDate(row.EffectiveFrom),
        Note = row.Note
    };

    internal static LoanRow ToRow(Loan value) => new()
    {
        Id = Key(value.Id),
        Name = value.Name,
        Bank = value.Bank,
        MonthlyInstallment = value.MonthlyInstallment,
        PaymentDay = value.PaymentDay,
        StartDate = FormatDate(value.StartDate),
        EndDate = FormatNullableDate(value.EndDate),
        InstallmentCount = value.InstallmentCount,
        RemainingDebt = value.RemainingDebt,
        EarlyClosureAmount = value.EarlyClosureAmount,
        IsActive = value.IsActive
    };

    private static Loan FromRow(LoanRow row) => new()
    {
        Id = ParseKey(row.Id),
        Name = row.Name,
        Bank = row.Bank,
        MonthlyInstallment = row.MonthlyInstallment,
        PaymentDay = row.PaymentDay,
        StartDate = ParseDate(row.StartDate),
        EndDate = ParseNullableDate(row.EndDate),
        InstallmentCount = row.InstallmentCount,
        RemainingDebt = row.RemainingDebt,
        EarlyClosureAmount = row.EarlyClosureAmount,
        IsActive = row.IsActive
    };

    internal static PaymentInstallmentRow ToRow(TemporaryPaymentInstallment value) => new()
    {
        Id = Key(value.Id),
        PlanId = Key(value.PlanId),
        DueDate = FormatDate(value.DueDate),
        Amount = value.Amount,
        IsPaid = value.IsPaid
    };

    private static TemporaryPaymentInstallment FromRow(PaymentInstallmentRow row) => new()
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
        CurrentTotalDebt = value.CurrentTotalDebt,
        LastStatementDebt = value.CarriedBalance,
        LastStatementRemaining = value.CarriedBalance,
        CurrentCycleSpending = value.UnbilledSpending,
        StatementClosingDay = value.StatementClosingDay,
        PaymentDueDay = value.PaymentDueDay,
        MinimumPaymentRate = value.MinimumPaymentRate,
        PaymentMode = (int)CreditCardPaymentMode.Minimum,
        ManualPaymentAmount = null,
        CarriedBalance = value.CarriedBalance,
        UnbilledSpending = value.UnbilledSpending,
        BalanceAsOfDate = FormatDate(value.BalanceAsOfDate),
        StatementModelVersion = 3,
        PaymentStrategy = (int)value.PaymentStrategy,
        FixedPaymentAmount = value.FixedPaymentAmount,
        ProjectionFallbackStrategy = (int)value.ProjectionFallbackStrategy,
        ProjectionFallbackFixedAmount = value.ProjectionFallbackFixedAmount
    };

    private static CreditCard FromRow(
        CreditCardRow row,
        IEnumerable<CardInstallmentRow> installments,
        IEnumerable<CreditCardPaymentPlanRow> paymentPlans) => new()
    {
        Id = ParseKey(row.Id),
        Name = row.Name,
        Bank = row.Bank,
        Limit = row.Limit,
        CurrentTotalDebt = row.CurrentTotalDebt,
        CarriedBalance = row.CarriedBalance,
        UnbilledSpending = row.UnbilledSpending,
        BalanceAsOfDate = ParseDate(row.BalanceAsOfDate),
        StatementClosingDay = row.StatementClosingDay,
        PaymentDueDay = row.PaymentDueDay,
        MinimumPaymentRate = row.MinimumPaymentRate,
        PaymentStrategy = (CreditCardPaymentStrategy)row.PaymentStrategy,
        FixedPaymentAmount = row.FixedPaymentAmount,
        ProjectionFallbackStrategy = (ProjectionFallbackStrategy)row.ProjectionFallbackStrategy,
        ProjectionFallbackFixedAmount = row.ProjectionFallbackFixedAmount,
        Charges = installments.Select(FromRow).OrderBy(x => x.PostingDate).ToArray(),
        PaymentPlans = paymentPlans.Select(FromRow).OrderBy(x => x.DueDate).ToArray()
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

    private static CreditCardPaymentPlanRow ToRow(CreditCardPaymentPlan value) => new()
    {
        Id = Key(value.Id),
        CreditCardId = Key(value.CreditCardId),
        DueDate = FormatDate(value.DueDate),
        PlannedPaymentAmount = value.Amount ?? 0m,
        PaymentType = (int)value.PaymentType,
        Amount = value.Amount
    };

    private static CreditCardPaymentPlan FromRow(CreditCardPaymentPlanRow row) => new()
    {
        Id = ParseKey(row.Id),
        CreditCardId = ParseKey(row.CreditCardId),
        DueDate = ParseDate(row.DueDate),
        PaymentType = (CreditCardPaymentType)row.PaymentType,
        Amount = row.Amount ?? (row.PlannedPaymentAmount > 0m ? row.PlannedPaymentAmount : null)
    };

    private static ExpenseRow ToRow(Expense value) => new()
    {
        Id = Key(value.Id),
        Amount = value.Amount,
        Date = FormatDate(value.Date),
        Category = (int)value.Category,
        PaymentType = (int)value.PaymentType,
        Note = value.Note,
        CreditCardId = value.CreditCardId?.ToString("D"),
        InstallmentCount = value.InstallmentCount,
        FirstInstallmentDate = FormatNullableDate(value.FirstInstallmentDate),
        CreatedAtUtc = value.CreatedAtUtc == default ? null : FormatInstant(value.CreatedAtUtc)
    };

    private static Expense FromRow(ExpenseRow row) => new()
    {
        Id = ParseKey(row.Id),
        Amount = row.Amount,
        Date = ParseDate(row.Date),
        Category = (ExpenseCategory)row.Category,
        PaymentType = (ExpensePaymentType)row.PaymentType,
        Note = row.Note,
        CreditCardId = string.IsNullOrWhiteSpace(row.CreditCardId) ? null : ParseKey(row.CreditCardId),
        InstallmentCount = row.InstallmentCount,
        FirstInstallmentDate = ParseNullableDate(row.FirstInstallmentDate),
        CreatedAtUtc = string.IsNullOrWhiteSpace(row.CreatedAtUtc)
            ? new DateTimeOffset(ParseDate(row.Date).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : ParseInstant(row.CreatedAtUtc)
    };

    internal static SpendableBalanceSnapshotRow ToRow(SpendableBalanceSnapshot value) => new()
    {
        Id = Key(value.Id),
        Amount = value.Amount,
        SnapshotDate = FormatDate(value.SnapshotDate),
        SalaryPeriodStart = FormatDate(value.SalaryPeriodStart),
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc),
        Note = value.Note
    };

    private static SpendableBalanceSnapshot FromRow(SpendableBalanceSnapshotRow row) => new()
    {
        Id = ParseKey(row.Id),
        Amount = row.Amount,
        SnapshotDate = ParseDate(row.SnapshotDate),
        SalaryPeriodStart = ParseDate(row.SalaryPeriodStart),
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc),
        Note = row.Note
    };

    private static EmergencyFundRow ToRow(EmergencyFund value) => new()
    {
        Id = Key(value.Id),
        TargetAmount = value.TargetAmount,
        CurrentAmount = value.CurrentAmount,
        PlannedPeriodContribution = value.PlannedPeriodContribution
    };

    private static EmergencyFund FromRow(EmergencyFundRow row) => new()
    {
        Id = ParseKey(row.Id),
        TargetAmount = row.TargetAmount,
        CurrentAmount = row.CurrentAmount,
        PlannedPeriodContribution = row.PlannedPeriodContribution
    };

    private static EmergencyFundTransferRow ToRow(EmergencyFundTransfer value) => new()
    {
        Id = Key(value.Id),
        TransferDate = FormatDate(value.TransferDate),
        SalaryPeriodStart = FormatDate(value.SalaryPeriodStart),
        Amount = value.Amount,
        CoveredPlannedAmount = value.CoveredPlannedAmount,
        CreatedAtUtc = FormatInstant(value.CreatedAtUtc)
    };

    private static EmergencyFundTransfer FromRow(EmergencyFundTransferRow row) => new()
    {
        Id = ParseKey(row.Id),
        TransferDate = ParseDate(row.TransferDate),
        SalaryPeriodStart = ParseDate(row.SalaryPeriodStart),
        Amount = row.Amount,
        CoveredPlannedAmount = row.CoveredPlannedAmount,
        CreatedAtUtc = ParseInstant(row.CreatedAtUtc)
    };

    private async Task MigrateLegacyCreditCardsAsync()
    {
        var cards = await _database.Table<CreditCardRow>().ToListAsync();
        foreach (var row in cards.Where(x => x.StatementModelVersion < 3))
        {
            if (row.StatementModelVersion < 2)
            {
                row.CarriedBalance = row.LastStatementRemaining > 0m
                    ? row.LastStatementRemaining
                    : row.LastStatementDebt;
                row.UnbilledSpending = row.CurrentCycleSpending;
                row.BalanceAsOfDate = FormatDate(_migrationDate);

                if (row.PaymentMode == (int)CreditCardPaymentMode.Manual && row.ManualPaymentAmount is > 0m)
                {
                    var close = CreditCardProjectionCalculator.ResolveStatementCloseOnOrAfter(
                        _migrationDate,
                        row.StatementClosingDay);
                    var due = CreditCardProjectionCalculator.ResolvePaymentDueDate(close, row.PaymentDueDay);
                    await _database.InsertOrReplaceAsync(ToRow(new CreditCardPaymentPlan
                    {
                        CreditCardId = ParseKey(row.Id),
                        DueDate = due,
                        PaymentType = CreditCardPaymentType.FixedAmount,
                        Amount = row.ManualPaymentAmount.Value
                    }));
                }
            }

            row.PaymentStrategy = (int)CreditCardPaymentStrategy.AskEachStatement;
            row.FixedPaymentAmount = null;
            row.ProjectionFallbackStrategy = (int)ProjectionFallbackStrategy.None;
            row.ProjectionFallbackFixedAmount = null;
            row.StatementModelVersion = 3;
            await _database.UpdateAsync(row);
        }
    }
}

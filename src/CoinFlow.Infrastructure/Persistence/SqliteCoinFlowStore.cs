using System.Globalization;
using CoinFlow.Application.Abstractions;
using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

public sealed class SqliteCoinFlowStore : ICoinFlowStore, IAsyncDisposable
{
    private const string DateFormat = "yyyy-MM-dd";
    private readonly SQLiteAsyncConnection _database;
    private readonly bool _seedDevelopmentData;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public SqliteCoinFlowStore(string databasePath, bool seedDevelopmentData)
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
            await _database.CreateTableAsync<ExpenseRow>();
            await _database.CreateTableAsync<SettingsRow>();
            await _database.CreateTableAsync<EmergencyFundRow>();

            if (await _database.Table<SettingsRow>().CountAsync() == 0)
            {
                await _database.InsertAsync(new SettingsRow
                {
                    SalaryDay = 10,
                    GamificationEnabled = true,
                    DevelopmentSeedEnabled = _seedDevelopmentData
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
            connection.DeleteAll<CreditCardRow>();
            connection.DeleteAll<ExpenseRow>();
            connection.DeleteAll<SalaryRow>();
            connection.DeleteAll<LoanRow>();
            connection.DeleteAll<EmergencyFundRow>();
            connection.DeleteAll<SettingsRow>();

            connection.Insert(new SettingsRow
            {
                SalaryDay = 10,
                GamificationEnabled = true,
                DevelopmentSeedEnabled = false
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
            DevelopmentSeedEnabled = row.DevelopmentSeedEnabled
        };
    }

    public async Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(new SettingsRow
        {
            SalaryDay = settings.SalaryDay,
            GamificationEnabled = settings.GamificationEnabled,
            DevelopmentSeedEnabled = settings.DevelopmentSeedEnabled
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
        return cards.Select(row => FromRow(row, installments.Where(x => x.CreditCardId == row.Id))).ToArray();
    }

    public async Task UpsertCreditCardAsync(CreditCard card, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.InsertOrReplaceAsync(ToRow(card));
        await _database.ExecuteAsync("DELETE FROM card_installments WHERE CreditCardId = ?", Key(card.Id));
        foreach (var installment in card.FutureInstallments)
        {
            await _database.InsertAsync(ToRow(installment with { CreditCardId = card.Id }));
        }
    }

    public async Task DeleteCreditCardAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _database.ExecuteAsync("DELETE FROM card_installments WHERE CreditCardId = ?", Key(id));
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
        LastStatementDebt = value.LastStatementDebt,
        LastStatementRemaining = value.LastStatementRemaining,
        CurrentCycleSpending = value.CurrentCycleSpending,
        StatementClosingDay = value.StatementClosingDay,
        PaymentDueDay = value.PaymentDueDay,
        MinimumPaymentRate = value.MinimumPaymentRate,
        PaymentMode = (int)value.PaymentMode,
        ManualPaymentAmount = value.ManualPaymentAmount
    };

    private static CreditCard FromRow(CreditCardRow row, IEnumerable<CardInstallmentRow> installments) => new()
    {
        Id = ParseKey(row.Id),
        Name = row.Name,
        Bank = row.Bank,
        Limit = row.Limit,
        CurrentTotalDebt = row.CurrentTotalDebt,
        LastStatementDebt = row.LastStatementDebt,
        LastStatementRemaining = row.LastStatementRemaining,
        CurrentCycleSpending = row.CurrentCycleSpending,
        StatementClosingDay = row.StatementClosingDay,
        PaymentDueDay = row.PaymentDueDay,
        MinimumPaymentRate = row.MinimumPaymentRate,
        PaymentMode = (CreditCardPaymentMode)row.PaymentMode,
        ManualPaymentAmount = row.ManualPaymentAmount,
        FutureInstallments = installments.Select(FromRow).OrderBy(x => x.DueDate).ToArray()
    };

    internal static CardInstallmentRow ToRow(CardInstallment value) => new()
    {
        Id = Key(value.Id),
        CreditCardId = Key(value.CreditCardId),
        Description = value.Description,
        DueDate = FormatDate(value.DueDate),
        Amount = value.Amount
    };

    private static CardInstallment FromRow(CardInstallmentRow row) => new()
    {
        Id = ParseKey(row.Id),
        CreditCardId = ParseKey(row.CreditCardId),
        Description = row.Description,
        DueDate = ParseDate(row.DueDate),
        Amount = row.Amount
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
        FirstInstallmentDate = FormatNullableDate(value.FirstInstallmentDate)
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
        FirstInstallmentDate = ParseNullableDate(row.FirstInstallmentDate)
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
}

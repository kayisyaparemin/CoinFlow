using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

internal static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(SQLiteAsyncConnection database)
    {
        var salaries = new[]
        {
            new SalaryScheduleEntry { NetAmount = 115_000m, EffectiveFrom = new DateOnly(2026, 1, 1), Note = "Başlangıç maaşı" },
            new SalaryScheduleEntry { NetAmount = 132_250m, EffectiveFrom = new DateOnly(2027, 1, 1), Note = "%15 zam" }
        };
        foreach (var salary in salaries)
        {
            await database.InsertAsync(SqliteCoinFlowStore.ToRow(salary));
        }

        var loans = new[]
        {
            new Loan
            {
                Name = "Garanti kredi", Bank = "Garanti", MonthlyInstallment = 14_501.23m,
                PaymentDay = 7, StartDate = new DateOnly(2026, 9, 7), InstallmentCount = 22
            },
            new Loan
            {
                Name = "On Dijital", Bank = "Burgan", MonthlyInstallment = 7_374.59m,
                PaymentDay = 18, StartDate = new DateOnly(2026, 9, 18), InstallmentCount = 9
            }
        };
        foreach (var loan in loans)
        {
            await database.InsertAsync(SqliteCoinFlowStore.ToRow(loan));
        }

        var cardId = Guid.NewGuid();
        var cardInstallments = new[]
        {
            (new DateOnly(2026, 9, 28), 15_538.36m),
            (new DateOnly(2026, 10, 30), 9_102.90m),
            (new DateOnly(2026, 11, 28), 2_624.55m)
        };
        const decimal statementRemaining = 35_201.77m;
        const decimal cycleSpending = 61_283.91m;
        var card = new CreditCard
        {
            Id = cardId,
            Name = "Axess",
            Bank = "Akbank",
            Limit = 200_000m,
            CurrentTotalDebt = statementRemaining + cycleSpending + cardInstallments.Sum(x => x.Item2),
            LastStatementDebt = statementRemaining,
            LastStatementRemaining = statementRemaining,
            CurrentCycleSpending = cycleSpending,
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentMode = CreditCardPaymentMode.Minimum
        };
        await database.InsertAsync(SqliteCoinFlowStore.ToRow(card));
        foreach (var (date, amount) in cardInstallments)
        {
            await database.InsertAsync(SqliteCoinFlowStore.ToRow(new CardInstallment
            {
                CreditCardId = cardId,
                Description = "Gelecek dönem taksiti",
                DueDate = date,
                Amount = amount
            }));
        }
    }
}

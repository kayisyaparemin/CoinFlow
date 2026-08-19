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
                Name = "İhtiyaç kredisi", Bank = "Garanti", MonthlyInstallment = 14_500m,
                PaymentDay = 15, StartDate = new DateOnly(2026, 8, 15), InstallmentCount = 18,
                RemainingDebt = 261_000m, EarlyClosureAmount = 244_000m
            },
            new Loan
            {
                Name = "İhtiyaç kredisi", Bank = "Burgan", MonthlyInstallment = 7_500m,
                PaymentDay = 20, StartDate = new DateOnly(2026, 8, 20), InstallmentCount = 12,
                RemainingDebt = 90_000m
            }
        };
        foreach (var loan in loans)
        {
            await database.InsertAsync(SqliteCoinFlowStore.ToRow(loan));
        }

        var planId = Guid.NewGuid();
        await database.InsertAsync(new PaymentPlanRow { Id = planId.ToString("D"), Name = "Geçici ödeme", Kind = (int)PaymentPlanKind.Temporary });
        var payments = new[]
        {
            (new DateOnly(2026, 9, 5), 28_167m),
            (new DateOnly(2026, 10, 5), 28_167m),
            (new DateOnly(2026, 11, 5), 55_492m)
        };
        foreach (var (date, amount) in payments)
        {
            await database.InsertAsync(SqliteCoinFlowStore.ToRow(new TemporaryPaymentInstallment
            {
                PlanId = planId,
                DueDate = date,
                Amount = amount
            }));
        }

        var cardId = Guid.NewGuid();
        var card = new CreditCard
        {
            Id = cardId,
            Name = "Bonus",
            Bank = "Garanti",
            Limit = 200_000m,
            CurrentTotalDebt = 118_100m,
            LastStatementDebt = 94_000m,
            LastStatementRemaining = 94_000m,
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentMode = CreditCardPaymentMode.Minimum
        };
        await database.InsertAsync(SqliteCoinFlowStore.ToRow(card));
        var cardInstallments = new[]
        {
            (new DateOnly(2026, 9, 25), 14_500m),
            (new DateOnly(2026, 10, 25), 8_000m),
            (new DateOnly(2026, 11, 25), 1_600m)
        };
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

        var demoExpenses = new[]
        {
            new Expense { Amount = 4_200m, Date = new DateOnly(2026, 8, 10), Category = ExpenseCategory.Grocery, PaymentType = ExpensePaymentType.Cash, Note = "Demo market" },
            new Expense { Amount = 8_000m, Date = new DateOnly(2026, 8, 13), Category = ExpenseCategory.Fuel, PaymentType = ExpensePaymentType.Cash, Note = "Demo yakıt" },
            new Expense { Amount = 4_033m, Date = new DateOnly(2026, 8, 18), Category = ExpenseCategory.Home, PaymentType = ExpensePaymentType.Cash, Note = "Demo ev" }
        };
        foreach (var expense in demoExpenses)
        {
            await database.InsertAsync(new ExpenseRow
            {
                Id = expense.Id.ToString("D"),
                Amount = expense.Amount,
                Date = SqliteCoinFlowStore.FormatDate(expense.Date),
                Category = (int)expense.Category,
                PaymentType = (int)expense.PaymentType,
                Note = expense.Note
            });
        }
    }
}

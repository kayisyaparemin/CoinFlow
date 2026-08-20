using CoinFlow.Domain.Models;
using SQLite;

namespace CoinFlow.Infrastructure.Persistence;

internal static class DevelopmentDataSeeder
{
    internal const int CurrentSeedVersion = 1;

    private static readonly Guid Salary2026Id =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Salary2027Id =
        Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid GarantiLoanId =
        Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid BurganLoanId =
        Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid EminevimPlanId =
        Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid AxessCardId =
        Guid.Parse("40000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(SQLiteAsyncConnection database)
    {
        var salaries = new[]
        {
            new SalaryScheduleEntry
            {
                Id = Salary2026Id,
                Amount = 115_000m,
                EffectiveDate = new DateOnly(2026, 1, 1),
                Description = "Maaş"
            },
            new SalaryScheduleEntry
            {
                Id = Salary2027Id,
                Amount = 132_250m,
                EffectiveDate = new DateOnly(2027, 1, 1),
                Description = "Planlanan maaş"
            }
        };
        foreach (var salary in salaries)
        {
            await database.InsertOrReplaceAsync(
                SqliteCoinFlowStore.ToRow(salary));
        }

        var loans = new[]
        {
            new Loan
            {
                Id = GarantiLoanId,
                Name = "borç kapama",
                Bank = "Garanti BBVA",
                MonthlyPayment = 14_501.23m,
                PaymentDay = 7,
                NextPaymentDate = new DateOnly(2026, 9, 7),
                RemainingInstallmentCount = 22,
                RemainingDebt = 190_188m
            },
            new Loan
            {
                Id = BurganLoanId,
                Name = "On Dijital",
                Bank = "Burgan Bank",
                MonthlyPayment = 7_374.59m,
                PaymentDay = 18,
                NextPaymentDate = new DateOnly(2026, 9, 18),
                RemainingInstallmentCount = 9,
                RemainingDebt = 55_777m
            }
        };
        foreach (var loan in loans)
        {
            await database.InsertOrReplaceAsync(
                SqliteCoinFlowStore.ToRow(loan));
        }

        await database.InsertOrReplaceAsync(new PaymentPlanRow
        {
            Id = EminevimPlanId.ToString("D"),
            Name = "Eminevim",
            Kind = (int)PaymentPlanKind.Temporary
        });
        var eminevim = new[]
        {
            ("30000000-0000-0000-0000-000000000011", new DateOnly(2026, 9, 20), 28_167.40m),
            ("30000000-0000-0000-0000-000000000012", new DateOnly(2026, 10, 20), 28_167.40m),
            ("30000000-0000-0000-0000-000000000013", new DateOnly(2026, 11, 20), 55_492.20m)
        };
        foreach (var (id, dueDate, amount) in eminevim)
        {
            await database.InsertOrReplaceAsync(new PaymentInstallmentRow
            {
                Id = id,
                PlanId = EminevimPlanId.ToString("D"),
                DueDate = SqliteCoinFlowStore.FormatDate(dueDate),
                Amount = amount
            });
        }

        var card = new CreditCard
        {
            Id = AxessCardId,
            Name = "Axess",
            Bank = "Akbank",
            Limit = 607_350m,
            CarriedBalance = 35_201.77m,
            UnbilledSpending = 61_283.91m,
            BalanceAsOfDate = new DateOnly(2026, 8, 20),
            StatementClosingDay = 25,
            PaymentDueDay = 5,
            MinimumPaymentRate = 0.40m,
            PaymentStrategy = CreditCardPaymentStrategy.AskEachStatement,
            ProjectionFallbackStrategy = ProjectionFallbackStrategy.Minimum
        };
        await database.InsertOrReplaceAsync(SqliteCoinFlowStore.ToRow(card));

        var cardCharges = new[]
        {
            ("40000000-0000-0000-0000-000000000011", new DateOnly(2026, 9, 28), 15_538.36m),
            ("40000000-0000-0000-0000-000000000012", new DateOnly(2026, 10, 30), 9_102.90m),
            ("40000000-0000-0000-0000-000000000013", new DateOnly(2026, 11, 28), 2_624.55m)
        };
        foreach (var (id, postingDate, amount) in cardCharges)
        {
            await database.InsertOrReplaceAsync(new CardInstallmentRow
            {
                Id = id,
                CreditCardId = AxessCardId.ToString("D"),
                Description = "Gelecek taksit",
                DueDate = SqliteCoinFlowStore.FormatDate(postingDate),
                Amount = amount
            });
        }
    }
}

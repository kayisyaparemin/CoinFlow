using CoinFlow.Application.Abstractions;
using CoinFlow.Application.Services;
using CoinFlow.Domain.Calculations;
using CoinFlow.Domain.Models;

namespace CoinFlow.Tests;

internal static class TestFactory
{
    public static FinancialProjectionCalculator ProjectionCalculator()
    {
        var salaryPeriods = new SalaryPeriodCalculator();
        var income = new IncomeProjectionCalculator(new SalaryResolver());
        var loans = new LoanScheduleCalculator();
        var scheduled = new ScheduledPaymentCalculator();
        var mandatory = new MandatoryPaymentCalculator(loans, scheduled);
        return new FinancialProjectionCalculator(
            salaryPeriods,
            income,
            new CreditCardStatementCalculator(),
            mandatory);
    }

    public static CoinFlowService Service(
        ICoinFlowStore store,
        DateOnly? today = null)
    {
        var projectionCalculator = ProjectionCalculator();
        var projectionService =
            new FinancialProjectionService(projectionCalculator);
        var installments = new InstallmentScheduleCalculator();
        return new CoinFlowService(
            store,
            new FixedClock(
                today ?? new DateOnly(2026, 8, 20),
                new DateTimeOffset(
                    2026, 8, 20, 12, 0, 0, TimeSpan.Zero)),
            projectionService,
            new SimulationCalculator(
                projectionCalculator,
                installments),
            new TargetAmountCalculator());
    }

    public static FinancialPlan CanonicalPlan() => new()
    {
        Settings = new UserSettings
        {
            SalaryDay = 10,
            MonthlyLivingBudget = 30_000m,
            ProjectionStartingSavings = 0m
        },
        Salaries =
        [
            new SalaryScheduleEntry
            {
                Amount = 115_000m,
                EffectiveDate = new DateOnly(2026, 1, 1),
                Description = "Maaş"
            },
            new SalaryScheduleEntry
            {
                Amount = 132_250m,
                EffectiveDate = new DateOnly(2027, 1, 1),
                Description = "Planlanan maaş"
            }
        ],
        Loans =
        [
            new Loan
            {
                Name = "borç kapama",
                Bank = "Garanti BBVA",
                MonthlyPayment = 14_501.23m,
                PaymentDay = 7,
                NextPaymentDate = new DateOnly(2026, 9, 7),
                RemainingInstallmentCount = 22
            },
            new Loan
            {
                Name = "On Dijital",
                Bank = "Burgan Bank",
                MonthlyPayment = 7_374.59m,
                PaymentDay = 18,
                NextPaymentDate = new DateOnly(2026, 9, 18),
                RemainingInstallmentCount = 9
            }
        ],
        PaymentPlans = [EminevimPlan()],
        CreditCards = [AxessCard()]
    };

    public static TemporaryPaymentPlan EminevimPlan()
    {
        var id = Guid.NewGuid();
        return new TemporaryPaymentPlan
        {
            Id = id,
            Name = "Eminevim",
            Kind = PaymentPlanKind.Temporary,
            Installments =
            [
                new TemporaryPaymentInstallment
                {
                    PlanId = id,
                    DueDate = new DateOnly(2026, 9, 20),
                    Amount = 28_167.40m
                },
                new TemporaryPaymentInstallment
                {
                    PlanId = id,
                    DueDate = new DateOnly(2026, 10, 20),
                    Amount = 28_167.40m
                },
                new TemporaryPaymentInstallment
                {
                    PlanId = id,
                    DueDate = new DateOnly(2026, 11, 20),
                    Amount = 55_492.20m
                }
            ]
        };
    }

    public static CreditCard AxessCard()
    {
        var id = Guid.NewGuid();
        return new CreditCard
        {
            Id = id,
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
            ProjectionFallbackStrategy =
                ProjectionFallbackStrategy.Minimum,
            Charges =
            [
                new CardCharge
                {
                    CreditCardId = id,
                    PostingDate = new DateOnly(2026, 9, 28),
                    Amount = 15_538.36m
                },
                new CardCharge
                {
                    CreditCardId = id,
                    PostingDate = new DateOnly(2026, 10, 30),
                    Amount = 9_102.90m
                },
                new CardCharge
                {
                    CreditCardId = id,
                    PostingDate = new DateOnly(2026, 11, 28),
                    Amount = 2_624.55m
                }
            ]
        };
    }

    private sealed class FixedClock(
        DateOnly today,
        DateTimeOffset utcNow) : IClock
    {
        public DateOnly Today { get; } = today;
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}


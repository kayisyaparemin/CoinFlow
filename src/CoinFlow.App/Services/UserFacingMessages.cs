namespace CoinFlow.App.Services;

public static class UserFacingMessages
{
    public const string GenericSaveFailure =
        "İşlem sırasında bir sorun oluştu. Tekrar deneyebilirsin.";

    public static string FromException(
        Exception exception,
        string fallback = GenericSaveFailure)
    {
        var message = exception.Message.Trim();
        if (exception is InvalidOperationException &&
            message.Length > 0 &&
            !LooksTechnical(message))
        {
            return message;
        }

        return fallback;
    }

    private static bool LooksTechnical(string message)
    {
        var technicalSignals = new[]
        {
            "exception",
            "sqlite",
            "sql",
            "database",
            "constraint",
            "stack",
            "system.",
            "microsoft.",
            "coinflow.",
            "sequence contains",
            "object reference",
            "value cannot be null",
            "parameter",
            "unable to resolve",
            "no service for type",
            "the given key"
        };

        return technicalSignals.Any(signal =>
            message.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }
}

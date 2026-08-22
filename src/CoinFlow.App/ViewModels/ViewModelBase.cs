using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoinFlow.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    protected static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool hasStatus;

    protected static string Money(decimal value, int decimals = 0) =>
        $"{value.ToString(decimals == 0 ? "N0" : "N2", TurkishCulture)} TL";

    protected static decimal ParseMoney(string? value, string fieldName)
    {
        if (decimal.TryParse(value, NumberStyles.Number, TurkishCulture, out var result) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result))
        {
            return result;
        }

        throw new InvalidOperationException($"{fieldName} geçerli bir tutar olmalıdır.");
    }

    protected static decimal ParsePositiveMoney(string? value, string fieldName)
    {
        var result = ParseMoney(value, fieldName);
        if (result <= 0m)
        {
            throw new InvalidOperationException(
                $"{fieldName} 0'dan büyük olmalıdır.");
        }

        return result;
    }

    protected void SetStatus(string message)
    {
        StatusMessage = message;
        HasStatus = !string.IsNullOrWhiteSpace(message);
    }
}

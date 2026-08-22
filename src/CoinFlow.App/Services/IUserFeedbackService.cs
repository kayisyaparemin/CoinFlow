namespace CoinFlow.App.Services;

public interface IUserFeedbackService
{
    Task ShowSuccessAsync(
        string message,
        string title = "Kaydedildi",
        string button = "Tamam");

    Task ShowErrorAsync(
        string message,
        string title = "Kaydedilemedi",
        string button = "Tamam");

    Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        string cancel);
}

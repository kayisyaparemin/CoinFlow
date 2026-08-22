using Microsoft.Maui.Controls;

namespace CoinFlow.App.Services;

public sealed class UserFeedbackService : IUserFeedbackService
{
    public Task ShowSuccessAsync(
        string message,
        string title = "Kaydedildi",
        string button = "Tamam") =>
        ShowAlertAsync(title, message, button);

    public Task ShowErrorAsync(
        string message,
        string title = "Kaydedilemedi",
        string button = "Tamam") =>
        ShowAlertAsync(title, message, button);

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        string cancel) =>
        CurrentPage().DisplayAlert(title, message, accept, cancel);

    private static Task ShowAlertAsync(
        string title,
        string message,
        string button) =>
        CurrentPage().DisplayAlert(title, message, button);

    private static Page CurrentPage()
    {
        Page? page = null;
        if (Shell.Current?.CurrentPage is { } shellPage)
        {
            page = shellPage;
        }
        else if (Microsoft.Maui.Controls.Application.Current?.Windows
                     .FirstOrDefault()?.Page is { } windowPage)
        {
            page = windowPage;
        }

        return page is null
            ? throw new InvalidOperationException("Geçerli ekran bulunamadı.")
            : ResolveTopPage(page);
    }

    private static Page ResolveTopPage(Page page)
    {
        while (page.Navigation.ModalStack.LastOrDefault() is { } modal)
        {
            page = modal;
        }

        return page switch
        {
            NavigationPage { CurrentPage: { } current } =>
                ResolveTopPage(current),
            FlyoutPage { Detail: { } detail } =>
                ResolveTopPage(detail),
            TabbedPage { CurrentPage: { } current } =>
                ResolveTopPage(current),
            _ => page
        };
    }
}

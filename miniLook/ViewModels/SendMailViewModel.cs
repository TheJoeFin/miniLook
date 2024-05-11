using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using miniLook.Contracts.Services;

namespace miniLook.ViewModels;

public partial class SendMailViewModel : ObservableRecipient
{
    [ObservableProperty]
    private string newSubject = string.Empty;

    [ObservableProperty]
    private string newBody = string.Empty;

    [ObservableProperty]
    private string recipient = string.Empty;

    INavigationService NavigationService { get; }

    public SendMailViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    [RelayCommand]
    public void GoBack() => NavigationService.GoBack();

    [RelayCommand]
    public async Task SendMail()
    {
        if (ProviderManager.Instance.GlobalProvider?.GetClient() is not GraphServiceClient graphClient)
            return;

        Message message = new()
        {
            Subject = NewSubject,
            Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = NewBody,
            },
            ToRecipients =
        [
            new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = Recipient,
                },
            },
        ],
        //    CcRecipients =
        //[
        //    new Recipient
        //    {
        //        EmailAddress = new EmailAddress
        //        {
        //            Address = "danas@contoso.com",
        //        },
        //    },
        //],
        };

        await graphClient.Me.SendMail(message, true).Request().PostAsync();

        NavigationService.GoBack();
    }
}

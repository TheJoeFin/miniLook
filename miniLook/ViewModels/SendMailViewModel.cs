using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using System.Collections.ObjectModel;
using System.Net.Mail;

namespace miniLook.ViewModels;

public partial class SendMailViewModel : ObservableRecipient, INavigationAware
{
    [ObservableProperty]
    private string newSubject = string.Empty;

    [ObservableProperty]
    private string newBody = string.Empty;

    [ObservableProperty]
    private string recipient = string.Empty;

    public ObservableCollection<EmailAddress> EmailAddresses { get; set; } = [];

    [ObservableProperty]
    private bool canSend = false;

    INavigationService NavigationService { get; }

    public SendMailViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    public readonly List<EmailAddress> SampleEmails = [
        new EmailAddress { Address = "me@example.com", Name="me" },
        new EmailAddress { Address = "you@example.com", Name="you" },
        new EmailAddress { Address = "joe@JoeFinApps.com", Name="Joe" },
        new EmailAddress { Address = "josephFinney@outlook.com", Name="Joseph Finney" },
        ];

    public ObservableCollection<EmailAddress> SuggestedRecipients { get; set; } = [];

    public void ClickedItem()
    {
        
    }

    partial void OnRecipientChanged(string value)
    {
        var emailsAsStrings = value.Split(';', StringSplitOptions.TrimEntries);
        EmailAddresses.Clear();

        foreach (var email in emailsAsStrings)
            if (!IsValidEmail(email))
                return;

        foreach (var email in emailsAsStrings)
            EmailAddresses.Add(new EmailAddress { Address = email.Trim() });

        CanSend = EmailAddresses.Count > 0 && !string.IsNullOrEmpty(NewBody);
    }

    partial void OnNewBodyChanged(string value)
    {
        CanSend = EmailAddresses.Count > 0 && !string.IsNullOrEmpty(value);
    }

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        string trimmedEmail = email.Trim();

        if (trimmedEmail.EndsWith('.'))
            return false; // suggested by @TK-421

        try
        {
            MailAddress mailAddress = new(email);
            return mailAddress.Address == trimmedEmail;
        }
        catch
        {
            return false;
        }
    }

    [RelayCommand]
    public void GoBack() => NavigationService.GoBack();

    [RelayCommand]
    public async Task SendMail()
    {
        foreach (EmailAddress email in EmailAddresses)
            if (!IsValidEmail(email.Address))
                return;

        List<Recipient> recipientList = [];

        foreach (EmailAddress email in EmailAddresses)
            recipientList.Add(new Recipient { EmailAddress = email });

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
            ToRecipients = recipientList,
        };

        await graphClient.Me.SendMail(message, true).Request().PostAsync();

        NavigationService.GoBack();
    }

    public async void OnNavigatedTo(object parameter)
    {
        await loadSuggestedEmails();
    }

    private async Task loadSuggestedEmails()
    {
        if (ProviderManager.Instance.GlobalProvider?.GetClient() is not GraphServiceClient graphClient)
            return;

        IUserPeopleCollectionPage recentPeople = await graphClient.Me.People.Request().GetAsync();

        SuggestedRecipients.Clear();

        foreach (Person person in recentPeople)
        {
            if (person.ScoredEmailAddresses == null
                || !person.ScoredEmailAddresses.Any()
                || string.IsNullOrWhiteSpace(person.ScoredEmailAddresses.First().Address))
                continue;

            EmailAddress newEmailAddress = new()
            {
                Address = person.ScoredEmailAddresses.First().Address,
                Name = person.DisplayName
            };
            SuggestedRecipients.Add(newEmailAddress);
        }
    }

    public void OnNavigatedFrom()
    {
    }
}

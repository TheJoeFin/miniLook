using CommunityToolkit.Authentication;
using CommunityToolkit.Graph.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace miniLook.ViewModels;

public partial class SendMailViewModel : ObservableRecipient, INavigationAware
{
    [ObservableProperty]
    private string newSubject = string.Empty;

    [ObservableProperty]
    private string newBody = string.Empty;

    [ObservableProperty]
    private string recipient = string.Empty;

    [ObservableProperty]
    private string recipientTextBox = string.Empty;

    private string _conversationId = string.Empty;
    private byte[] _previousConversationalIndex = [];

    public ObservableCollection<EmailAddress> EmailAddresses { get; set; } = [];

    [ObservableProperty]
    private bool canSend = false;

    INavigationService NavigationService { get; }

    public SendMailViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    public ObservableCollection<EmailAddress> SuggestedRecipients { get; set; } = [];

    public void TryAddThisClickedItem(string clicked)
    {
        if (!IsValidEmail(clicked))
            return;

        RecipientTextBox = string.Empty;

        try
        {
            EmailAddresses.Add(new EmailAddress { Address = clicked });
        }
        catch (Exception)
        {
            Debug.WriteLine($"Failed to add {clicked} to emails");
        }
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

        Regex emailRegex = EmailRegex();
        return emailRegex.IsMatch(trimmedEmail);
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

        if (!string.IsNullOrWhiteSpace(_conversationId))
            message.ConversationId = _conversationId;

        // TODO use the conversation index to reply to the email in a thread

        await graphClient.Me.SendMail(message, true).Request().PostAsync();

        NavigationService.GoBack();
    }

    public async void OnNavigatedTo(object parameter)
    {
        await loadSuggestedEmails();

        if (parameter is MailData replying)
        {
            if (replying.Subject.StartsWith("Re: "))
                NewSubject = $"{replying.Subject}";
            else
                NewSubject = $"Re: {replying.Subject}";

            EmailAddresses.Add(new EmailAddress { Address = replying.Sender });
            _conversationId = replying.ConversationId;
        }
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

    [GeneratedRegex(@"^([\w\.\-]+)@([\w\-]+)((\.(\w){2,3})+)$")]
    private static partial Regex EmailRegex();
}

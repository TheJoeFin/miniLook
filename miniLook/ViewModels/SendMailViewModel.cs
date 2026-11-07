using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using miniLook.Contracts.Services;
using miniLook.Contracts.ViewModels;
using miniLook.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using miniLook.Core.Helpers;
using miniLook.Helpers;

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

    private MailData? messageReplyingTo;

    public ObservableCollection<EmailAddress> EmailAddresses { get; set; } = [];


    [ObservableProperty]
    private bool canSend = false;

    private string _validationError = string.Empty;
    public string ValidationError
    {
        get => _validationError;
        set
        {
            if (SetProperty(ref _validationError, value))
                OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    INavigationService NavigationService { get; }

    IGraphService GraphService { get; }

    public SendMailViewModel(INavigationService navigationService, IGraphService graphService)
    {
        NavigationService = navigationService;
        GraphService = graphService;
        EmailAddresses.CollectionChanged += (_, _) => UpdateCanSend();
    }

    public ObservableCollection<EmailAddress> SuggestedRecipients { get; set; } = [];

    public void TryAddThisClickedItem(string clicked)
    {
        EmailAddress? email = CreateRecipient(clicked);
        if (email is null)
            return;

        RecipientTextBox = string.Empty;
        EmailAddresses.Add(email);
    }

    public EmailAddress? CreateRecipient(string text)
    {
        if (!IsValidEmail(text))
        {
            ValidationError = "Mail_InvalidRecipient".GetLocalized();
            Debug.WriteLine("[Mail] An invalid recipient address was rejected.");
            return null;
        }
        ValidationError = string.Empty;
        return new EmailAddress { Address = text.Trim() };
    }

    partial void OnRecipientChanged(string value)
    {
        var emailsAsStrings = value.Split(';', StringSplitOptions.TrimEntries);
        EmailAddresses.Clear();

        foreach (var email in emailsAsStrings)
            if (!IsValidEmail(email))
            {
                ValidationError = "Mail_InvalidRecipient".GetLocalized();
                Debug.WriteLine("[Mail] The recipient list contains an invalid address.");
                return;
            }

        foreach (var email in emailsAsStrings)
            EmailAddresses.Add(new EmailAddress { Address = email.Trim() });

        ValidationError = string.Empty;
        UpdateCanSend();
    }

    partial void OnNewBodyChanged(string value)
    {
        UpdateCanSend();
    }

    private void UpdateCanSend() => CanSend = EmailAddresses.Count > 0 && !string.IsNullOrEmpty(NewBody);

    private static bool IsValidEmail(string? email) => EmailValidation.IsValidAddress(email);

    private bool ValidateRecipients()
    {
        if (EmailAddresses.Any(email => !IsValidEmail(email.Address)))
        {
            ValidationError = "Mail_InvalidRecipient".GetLocalized();
            Debug.WriteLine("[Mail] Sending was blocked by an invalid recipient address.");
            return false;
        }
        ValidationError = string.Empty;
        return true;
    }

    [RelayCommand]
    public void GoBack() => NavigationService.GoBack();

    [RelayCommand]
    public async Task SendMail()
    {
        if (!CanSend)
            return;

        if (!ValidateRecipients())
            return;

        List<Recipient> recipientList = [];

        foreach (EmailAddress email in EmailAddresses)
            recipientList.Add(new Recipient { EmailAddress = email });

        GraphServiceClient? graphClient = GraphService.Client;
        if (graphClient is null)
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

        await SendAndCloseAsync(() => messageReplyingTo is not null
            ? graphClient.Me.Messages[messageReplyingTo.Id].Reply
                .PostAsync(new Microsoft.Graph.Me.Messages.Item.Reply.ReplyPostRequestBody
                {
                    Message = message
                })
            : graphClient.Me.SendMail
                .PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                }));
    }

    /// <summary>Leaves the page only after the send worked. On failure the draft stays open with a reason.</summary>
    private async Task SendAndCloseAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (SignInRequiredException)
        {
            Debug.WriteLine("[Mail] The message was not sent because the sign-in expired.");
            ValidationError = "Mail_SendSignInRequired".GetLocalized();
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Mail] The message could not be sent: {ex.Message}");
            ValidationError = "Mail_SendFailed".GetLocalized();
            return;
        }

        NavigationService.GoBack();
    }

    [RelayCommand]
    public async Task ForwardMail()
    {
        if (!CanSend)
            return;

        if (!ValidateRecipients())
            return;

        List<Recipient> recipientList = new();

        foreach (EmailAddress email in EmailAddresses)
            recipientList.Add(new Recipient { EmailAddress = email });

        GraphServiceClient? graphClient = GraphService.Client;
        if (graphClient is null)
            return;

        Message message = new()
        {
            Subject = $"Fwd: {NewSubject}",
            Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = NewBody,
            },
            ToRecipients = recipientList,
        };

        await SendAndCloseAsync(() => graphClient.Me.SendMail
            .PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            }));
    }


    public async void OnNavigatedTo(object parameter)
    {
        if (parameter is (MailData mailData, MessageActionFlag action))
        {
            switch (action)
            {
                case MessageActionFlag.Reply:
                    if (mailData.Subject.StartsWith("Re: "))
                        NewSubject = $"{mailData.Subject}";
                    else
                        NewSubject = $"Re: {mailData.Subject}";

                    EmailAddresses.Add(new EmailAddress { Address = mailData.SenderAddress, Name = mailData.SenderName });
                    messageReplyingTo = mailData;
                    break;

                case MessageActionFlag.Forward:
                    NewSubject = $"Fwd: {mailData.Subject}";
                    NewBody = $"Forwarded message:\n\n{mailData.Body}";
                    break;
            }
        }
        try
        {
            await loadSuggestedEmails();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Mail] Suggested recipients could not be loaded: {ex.Message}");
            ValidationError = "Mail_ContactsUnavailable".GetLocalized();
        }
    }


    private async Task loadSuggestedEmails()
    {
        GraphServiceClient? graphClient = GraphService.Client;
        if (graphClient is null)
            return;

        var peopleResponse = await graphClient.Me.People.GetAsync();

        SuggestedRecipients.Clear();

        if (peopleResponse?.Value is null)
            return;

        foreach (Person person in peopleResponse.Value)
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
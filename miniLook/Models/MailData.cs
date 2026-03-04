using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;
using Microsoft.Graph.Models;
using System.Text.Json.Serialization;

namespace miniLook.Models;

public partial class MailData : ObservableObject, IJsonOnDeserialized
{
    public string Id { get; set; } = string.Empty;

    [ObservableProperty]
    public bool isRead = false;

    public string ConversationId { get; set; } = string.Empty;

    public string Sender { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string SenderAddress { get; set; } = string.Empty;

    public string SenderDisplayName => string.IsNullOrEmpty(SenderName) ? SenderAddress : SenderName;

    public string ToRecipientsFull { get; set; } = string.Empty;

    public string CcRecipientsFull { get; set; } = string.Empty;

    public bool HasToRecipients { get; set; } = false;

    public bool HasCcRecipients { get; set; } = false;

    public string Subject { get; set; } = $"No subject";

    public string Body { get; set; } = string.Empty;

    public bool HasHtmlBody { get; set; } = false;

    public string WebLink { get; set; } = string.Empty;

    public bool IsFocused { get; set; } = true;

    public bool IsEvent { get; set; } = false;

    public int AttachmentsCount { get; set; } = 0;

    public bool HasAttachments => AttachmentsCount > 0;

    public List<AttachmentInfo> Attachments { get; set; } = [];

    public DateTimeOffset ReceivedDateTime { get; set; } = DateTimeOffset.MinValue;

    public string RelativeReceivedDateTime => ReceivedDateTime != DateTimeOffset.MinValue
        ? ReceivedDateTime.Humanize()
        : string.Empty;

    public string FormattedReceivedDateTime => ReceivedDateTime != DateTimeOffset.MinValue
        ? ReceivedDateTime.ToLocalTime().ToString("ddd, MMM d, yyyy h:mm tt")
        : string.Empty;

    public MailData()
    {
        
    }

    public MailData(Message message)
    {
        Id = message.Id ?? string.Empty;
        IsRead = message.IsRead is true;
        SenderName = message.Sender?.EmailAddress?.Name ?? string.Empty;
        SenderAddress = message.Sender?.EmailAddress?.Address ?? string.Empty;
        Sender = string.IsNullOrEmpty(SenderName) ? SenderAddress : $"{SenderName} ({SenderAddress})";
        (_, ToRecipientsFull) = BuildRecipientDisplay(message.ToRecipients);
        HasToRecipients = message.ToRecipients?.Count > 0;
        (_, CcRecipientsFull) = BuildRecipientDisplay(message.CcRecipients);
        HasCcRecipients = message.CcRecipients?.Count > 0;
        Subject = message.Subject ?? $"No subject";
        WebLink = message.WebLink ?? string.Empty;
        ReceivedDateTime = message.ReceivedDateTime ?? DateTimeOffset.MinValue;
        ConversationId = message.ConversationId ?? string.Empty;
        IsFocused = message.InferenceClassification != InferenceClassificationType.Other;

        Body = message.BodyPreview ?? string.Empty;

        HasHtmlBody = !string.IsNullOrEmpty(message.BodyPreview);
    }

    public void OnDeserialized()
    {
        if (!string.IsNullOrEmpty(SenderName) || string.IsNullOrEmpty(Sender))
            return;

        int parenStart = Sender.LastIndexOf('(');
        int parenEnd = Sender.LastIndexOf(')');
        if (parenStart > 0 && parenEnd > parenStart)
        {
            SenderName = Sender[..parenStart].Trim();
            SenderAddress = Sender[(parenStart + 1)..parenEnd];
        }
        else
        {
            SenderName = Sender;
            SenderAddress = Sender;
        }
    }

    private static (string Short, string Full) BuildRecipientDisplay(List<Recipient>? recipients)
    {
        if (recipients is null || recipients.Count == 0)
            return (string.Empty, string.Empty);

        List<string> names = recipients
            .Select(r => r.EmailAddress?.Name ?? r.EmailAddress?.Address ?? string.Empty)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        List<string> fulls = recipients
            .Select(r =>
            {
                string name = r.EmailAddress?.Name ?? string.Empty;
                string addr = r.EmailAddress?.Address ?? string.Empty;
                return string.IsNullOrEmpty(name) ? addr : $"{name} ({addr})";
            })
            .ToList();

        string shortDisplay = names.Count <= 2
            ? string.Join(", ", names)
            : $"{names[0]}, {names[1]}, +{names.Count - 2} more";

        return (shortDisplay, string.Join(", ", fulls));
    }

    public override bool Equals(object? obj)
    {
        if (obj is not MailData mailData)
            return false;

        return Id == mailData.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}

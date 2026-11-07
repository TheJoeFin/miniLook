using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;
using Microsoft.Graph.Models;
using System.Text.Json.Serialization;

namespace miniLook.Models;

public partial class MailData : ObservableObject, IJsonOnDeserialized
{
    private bool _isRead;

    public string Id { get; set; } = string.Empty;

    // Written by hand on purpose: MailCacheJsonContext's source generator cannot see properties
    // emitted by the MVVM generator, so an [ObservableProperty] here is silently left out of the cache.
    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

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

    /// <summary>Graph says this message has attachments, and their listing has not been fetched yet.</summary>
    public bool AttachmentsPending { get; set; } = false;

    public bool HasAttachments => AttachmentsCount > 0 || AttachmentsPending;

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
        IsEvent = message.OdataType == "#microsoft.graph.eventMessageRequest";
        AttachmentsPending = message.HasAttachments is true;
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

    public MailData CreateCacheSnapshot() => new()
    {
        Id = Id,
        IsRead = IsRead,
        ConversationId = ConversationId,
        Sender = Sender,
        SenderName = SenderName,
        SenderAddress = SenderAddress,
        ToRecipientsFull = ToRecipientsFull,
        CcRecipientsFull = CcRecipientsFull,
        HasToRecipients = HasToRecipients,
        HasCcRecipients = HasCcRecipients,
        Subject = Subject,
        Body = Body,
        HasHtmlBody = HasHtmlBody,
        WebLink = WebLink,
        IsFocused = IsFocused,
        IsEvent = IsEvent,
        AttachmentsCount = AttachmentsCount,
        AttachmentsPending = AttachmentsPending,
        ReceivedDateTime = ReceivedDateTime,
        Attachments = Attachments.Select(a => new AttachmentInfo
        {
            Id = a.Id,
            Name = a.Name,
            ContentType = a.ContentType
        }).ToList()
    };

    public bool UpdateMetadata(MailData updated, bool? hasAttachments)
    {
        bool changed = IsRead != updated.IsRead || IsFocused != updated.IsFocused;
        bool metadataChanged = Subject != updated.Subject || Sender != updated.Sender || ReceivedDateTime != updated.ReceivedDateTime
            || Body != updated.Body || WebLink != updated.WebLink || ConversationId != updated.ConversationId
            || ToRecipientsFull != updated.ToRecipientsFull || CcRecipientsFull != updated.CcRecipientsFull
            || IsEvent != updated.IsEvent || HasHtmlBody != updated.HasHtmlBody
            || HasToRecipients != updated.HasToRecipients || HasCcRecipients != updated.HasCcRecipients;
        IsRead = updated.IsRead;
        IsFocused = updated.IsFocused;
        if (metadataChanged)
        {
            Subject = updated.Subject;
            Sender = updated.Sender;
            SenderName = updated.SenderName;
            SenderAddress = updated.SenderAddress;
            ReceivedDateTime = updated.ReceivedDateTime;
            Body = updated.Body;
            HasHtmlBody = updated.HasHtmlBody;
            WebLink = updated.WebLink;
            ConversationId = updated.ConversationId;
            ToRecipientsFull = updated.ToRecipientsFull;
            CcRecipientsFull = updated.CcRecipientsFull;
            HasToRecipients = updated.HasToRecipients;
            HasCcRecipients = updated.HasCcRecipients;
            IsEvent = updated.IsEvent;
            foreach (string property in new[]
            {
                    nameof(Subject), nameof(Sender), nameof(SenderName), nameof(SenderAddress), nameof(SenderDisplayName),
                    nameof(ReceivedDateTime), nameof(RelativeReceivedDateTime), nameof(FormattedReceivedDateTime),
                    nameof(Body), nameof(HasHtmlBody), nameof(WebLink), nameof(ToRecipientsFull), nameof(CcRecipientsFull),
                    nameof(HasToRecipients), nameof(HasCcRecipients), nameof(IsEvent)
                })
                OnPropertyChanged(property);
        }
        if (hasAttachments == false && HasAttachments)
        {
            SetAttachments([], 0);
            changed = true;
        }
        else if (hasAttachments == true && !HasAttachments)
        {
            AttachmentsPending = true;
            OnPropertyChanged(nameof(HasAttachments));
            changed = true;
        }
        return changed || metadataChanged;
    }

    public void SetAttachments(List<AttachmentInfo> attachments, int totalCount)
    {
        Attachments = attachments;
        AttachmentsCount = totalCount;
        AttachmentsPending = false;
        OnPropertyChanged(nameof(Attachments));
        OnPropertyChanged(nameof(AttachmentsCount));
        OnPropertyChanged(nameof(HasAttachments));
    }

    public bool ApplyDelta(Message message)
    {
        MailData updated = new(message);
        if (!message.IsRead.HasValue)
            updated.IsRead = IsRead;
        if (!message.InferenceClassification.HasValue)
            updated.IsFocused = IsFocused;
        if (message.Sender is null)
        {
            updated.Sender = Sender;
            updated.SenderName = SenderName;
            updated.SenderAddress = SenderAddress;
        }
        if (message.Subject is null)
            updated.Subject = Subject;
        if (message.BodyPreview is null)
        {
            updated.Body = Body;
            updated.HasHtmlBody = HasHtmlBody;
        }
        if (message.WebLink is null)
            updated.WebLink = WebLink;
        if (message.ConversationId is null)
            updated.ConversationId = ConversationId;
        if (!message.ReceivedDateTime.HasValue)
            updated.ReceivedDateTime = ReceivedDateTime;
        if (message.ToRecipients is null)
        {
            updated.ToRecipientsFull = ToRecipientsFull;
            updated.HasToRecipients = HasToRecipients;
        }
        if (message.CcRecipients is null)
        {
            updated.CcRecipientsFull = CcRecipientsFull;
            updated.HasCcRecipients = HasCcRecipients;
        }
        updated.IsEvent = message.OdataType is null ? IsEvent : message.OdataType == "#microsoft.graph.eventMessageRequest";
        return UpdateMetadata(updated, message.HasAttachments);
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
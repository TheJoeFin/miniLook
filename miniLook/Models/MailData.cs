﻿using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Graph;
using System.Text.Json.Serialization;

namespace miniLook.Models;

public partial class MailData: ObservableRecipient
{
    public string Id { get; set; } = string.Empty;

    [ObservableProperty]
    public bool isRead = false;

    public string ConversationId { get; set; } = string.Empty;
    
    public byte[] ConversationIndex { get; private set; }

    public string Sender { get; set; } = $"empty@example.com";

    public string Subject { get; set; } = $"No subject";

    public string Body { get; set; } = string.Empty;

    public string WebLink { get; set; } = string.Empty;

    public DateTimeOffset ReceivedDateTime { get; set; } = DateTimeOffset.MinValue;

    [JsonIgnore]
    public Message? GraphMessage { get; set; }

    public MailData()
    {
        
    }

    public MailData(Message message)
    {
        Id = message.Id;
        IsRead = message.IsRead is true;
        Sender = message.Sender?.EmailAddress?.Address ?? $"unknown sender";
        Subject = message.Subject ?? $"No subject";
        GraphMessage = message;
        WebLink = message.WebLink;
        ReceivedDateTime = message.ReceivedDateTime ?? DateTimeOffset.MinValue;
        ConversationId = message.ConversationId;
        ConversationIndex = message.ConversationIndex;

        // look into maybe using TinyHtml.WPF
        Body = message.BodyPreview;
        if (message.Body?.ContentType == BodyType.Text)
            Body = message.Body.Content;
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

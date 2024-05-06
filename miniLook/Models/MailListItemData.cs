using Microsoft.Graph;
using System.Text.Json.Serialization;

namespace miniLook.Models;
internal struct MailListItemData
{
    public string Id { get; set; } = string.Empty;

    public bool IsRead { get; set; } = false;

    public string Sender { get; set; } = $"empty@example.com";

    public string Subject { get; set; } = $"No subject";

    [JsonIgnore]
    public Message? GraphMessage { get; set; }

    public MailListItemData()
    {
        
    }

    public MailListItemData(Message message)
    {
        Id = message.Id;
        IsRead = message.IsRead is true;
        Sender = message.Sender?.EmailAddress?.Address ?? $"unknown sender";
        Subject = message.Subject ?? $"No subject";
        GraphMessage = message;
    }
}

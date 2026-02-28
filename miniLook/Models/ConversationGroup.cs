using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace miniLook.Models;

public partial class ConversationGroup : ObservableRecipient
{
    public string ConversationId { get; set; } = string.Empty;

    [ObservableProperty]
    private bool isExpanded = false;

    public ObservableCollection<MailData> Messages { get; } = [];

    public MailData LatestMessage => Messages[0];

    public string Subject => LatestMessage.Subject;

    public string Sender => LatestMessage.Sender;

    public DateTimeOffset ReceivedDateTime => LatestMessage.ReceivedDateTime;

    public bool HasAttachments => Messages.Any(m => m.HasAttachments);

    public bool IsEvent => Messages.Any(m => m.IsEvent);

    public int MessageCount => Messages.Count;

    public bool HasMultipleMessages => Messages.Count > 1;

    public ConversationGroup(string conversationId, IEnumerable<MailData> messages)
    {
        ConversationId = conversationId;
        foreach (var msg in messages.OrderByDescending(m => m.ReceivedDateTime))
            Messages.Add(msg);
    }
}

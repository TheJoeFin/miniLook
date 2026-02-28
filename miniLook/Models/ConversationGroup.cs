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

    public bool IsFocused => LatestMessage.IsFocused;

    public ConversationGroup(string conversationId, IEnumerable<MailData> messages)
    {
        ConversationId = conversationId;
        foreach (MailData? msg in messages.OrderByDescending(m => m.ReceivedDateTime))
            Messages.Add(msg);
    }

    public void SyncMessages(List<MailData> updatedMessages)
    {
        HashSet<string> updatedIds = updatedMessages.Select(m => m.Id).ToHashSet();

        // Remove messages no longer present
        for (int i = Messages.Count - 1; i >= 0; i--)
        {
            if (!updatedIds.Contains(Messages[i].Id))
                Messages.RemoveAt(i);
        }

        // Add new messages and ensure correct order
        for (int i = 0; i < updatedMessages.Count; i++)
        {
            int currentIndex = -1;
            for (int j = 0; j < Messages.Count; j++)
            {
                if (Messages[j].Id == updatedMessages[i].Id)
                {
                    currentIndex = j;
                    break;
                }
            }

            if (currentIndex < 0)
                Messages.Insert(i, updatedMessages[i]);
            else if (currentIndex != i)
                Messages.Move(currentIndex, i);
        }

        OnPropertyChanged(nameof(LatestMessage));
        OnPropertyChanged(nameof(Subject));
        OnPropertyChanged(nameof(Sender));
        OnPropertyChanged(nameof(ReceivedDateTime));
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(IsEvent));
        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(HasMultipleMessages));
        OnPropertyChanged(nameof(IsFocused));
    }
}

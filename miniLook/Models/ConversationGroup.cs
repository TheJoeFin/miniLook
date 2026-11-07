using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace miniLook.Models;

public partial class ConversationGroup : ObservableObject
{
    public string ConversationId { get; set; } = string.Empty;

    [ObservableProperty]
    private bool isExpanded = false;

    [ObservableProperty]
    private bool isHovered = false;

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
    private Summary _lastSummary;

    private readonly record struct Summary(MailData Latest, string Subject, string Sender, DateTimeOffset Received,
        bool Attachments, bool Event, int Count, bool Focused);

    private Summary GetSummary() => new(LatestMessage, Subject, Sender, ReceivedDateTime, HasAttachments, IsEvent, MessageCount, IsFocused);

    public ConversationGroup(string conversationId, IEnumerable<MailData> messages)
    {
        ConversationId = conversationId;
        foreach (MailData? msg in messages.OrderByDescending(m => m.ReceivedDateTime))
            Messages.Add(msg);
        _lastSummary = GetSummary();
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
            if (i < Messages.Count && ReferenceEquals(Messages[i], updatedMessages[i]))
                continue;
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

        Summary current = GetSummary();
        if (!ReferenceEquals(current.Latest, _lastSummary.Latest))
            OnPropertyChanged(nameof(LatestMessage));
        if (current.Subject != _lastSummary.Subject)
            OnPropertyChanged(nameof(Subject));
        if (current.Sender != _lastSummary.Sender)
            OnPropertyChanged(nameof(Sender));
        if (current.Received != _lastSummary.Received)
            OnPropertyChanged(nameof(ReceivedDateTime));
        if (current.Attachments != _lastSummary.Attachments)
            OnPropertyChanged(nameof(HasAttachments));
        if (current.Event != _lastSummary.Event)
            OnPropertyChanged(nameof(IsEvent));
        if (current.Count != _lastSummary.Count)
        {
            OnPropertyChanged(nameof(MessageCount));
            if ((current.Count > 1) != (_lastSummary.Count > 1))
                OnPropertyChanged(nameof(HasMultipleMessages));
        }
        if (current.Focused != _lastSummary.Focused)
            OnPropertyChanged(nameof(IsFocused));
        _lastSummary = current;
    }
}
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Graph.Models;
using miniLook.Models;
using AttachmentInfo = miniLook.Models.AttachmentInfo;

namespace miniLook.Core.Tests;

public class MailCacheRoundTripTests
{
    private static List<MailData> RoundTrip(MailData mail)
    {
        string json = JsonSerializer.Serialize([mail.CreateCacheSnapshot()], MailCacheJsonContext.Default.ListMailData);
        return JsonSerializer.Deserialize(json, MailCacheJsonContext.Default.ListMailData)!;
    }

    [Fact]
    public void ReadStateSurvivesTheCacheRoundTrip()
    {
        MailData mail = new() { Id = "read", ConversationId = "conversation", IsRead = true };

        Assert.True(RoundTrip(mail)[0].IsRead);
    }

    [Fact]
    public void EveryWritablePropertyIsSavedInTheCache()
    {
        // The JSON source generator cannot see properties made by other generators such as [ObservableProperty],
        // and it drops them without any warning. This fails if one is ever added to MailData.
        string json = JsonSerializer.Serialize([new MailData()], MailCacheJsonContext.Default.ListMailData);
        List<string> expected = typeof(MailData)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(property => property.Name)
            .ToList();

        Assert.Contains(nameof(MailData.IsRead), expected);
        Assert.All(expected, name => Assert.Contains($"\"{name}\"", json));
    }

    [Fact]
    public void EveryWritablePropertyIsCopiedIntoTheCacheSnapshot()
    {
        MailData mail = new()
        {
            Id = "id",
            IsRead = true,
            ConversationId = "conversation",
            Sender = "sender",
            SenderName = "name",
            SenderAddress = "address",
            ToRecipientsFull = "to",
            CcRecipientsFull = "cc",
            HasToRecipients = true,
            HasCcRecipients = true,
            Subject = "subject",
            Body = "body",
            HasHtmlBody = true,
            WebLink = "link",
            IsFocused = false,
            IsEvent = true,
            AttachmentsCount = 2,
            AttachmentsPending = true,
            ReceivedDateTime = Mail.Now,
        };

        MailData snapshot = mail.CreateCacheSnapshot();

        foreach (PropertyInfo property in typeof(MailData).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.Name != nameof(MailData.Attachments)))
        {
            Assert.Equal(property.GetValue(mail), property.GetValue(snapshot));
        }
    }

    [Fact]
    public void AttachmentsWaitingToBeListedAreRememberedAcrossARestart()
    {
        MailData mail = new(new Message { Id = "one", HasAttachments = true });

        MailData restored = RoundTrip(mail)[0];

        Assert.True(restored.AttachmentsPending);
        Assert.True(restored.HasAttachments);
    }

    [Fact]
    public void ACacheFromAnOlderVersionStillLoads()
    {
        // Written by the released app: it had no AttachmentsPending, and it did save IsRead.
        const string json = """
            [{ "Id": "old", "IsRead": true, "ConversationId": "c", "AttachmentsCount": 1,
               "Attachments": [{ "Id": "a", "Name": "file.pdf", "ContentType": "application/pdf" }],
               "ReceivedDateTime": "2026-10-01T10:00:00+00:00", "HasAttachments": true, "SenderDisplayName": "ignored" }]
            """;

        MailData restored = JsonSerializer.Deserialize(json, MailCacheJsonContext.Default.ListMailData)![0];

        Assert.True(restored.IsRead);
        Assert.False(restored.AttachmentsPending);
        Assert.True(restored.HasAttachments);
        Assert.Equal("file.pdf", Assert.Single(restored.Attachments).Name);
    }

    [Fact]
    public void NewMailShowsItsAttachmentIconBeforeTheListingArrives()
    {
        MailData mail = new(new Message { Id = "one", HasAttachments = true });
        List<string?> notifications = [];
        mail.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.True(mail.HasAttachments);
        Assert.Equal(0, mail.AttachmentsCount);

        mail.SetAttachments([new AttachmentInfo { Id = "a" }], 1);

        Assert.False(mail.AttachmentsPending);
        Assert.True(mail.HasAttachments);
        Assert.Contains(nameof(MailData.Attachments), notifications);
        Assert.Contains(nameof(MailData.HasAttachments), notifications);
    }

    [Fact]
    public void AListingThatFindsNothingClearsTheAttachmentIcon()
    {
        MailData mail = new(new Message { Id = "one", HasAttachments = true });

        mail.SetAttachments([], 0);

        Assert.False(mail.HasAttachments);
    }

    [Fact]
    public void AnUpdateSayingAttachmentsWereRemovedClearsThem()
    {
        MailData mail = new(new Message { Id = "one" });
        mail.SetAttachments([new AttachmentInfo { Id = "a" }], 1);

        Assert.True(mail.ApplyDelta(new Message { Id = "one", HasAttachments = false }));

        Assert.False(mail.HasAttachments);
        Assert.Empty(mail.Attachments);
    }

    [Fact]
    public void AnUpdateThatDoesNotMentionAttachmentsLeavesThemAlone()
    {
        MailData mail = new(new Message { Id = "one" });
        mail.SetAttachments([new AttachmentInfo { Id = "a" }], 1);

        mail.ApplyDelta(new Message { Id = "one", IsRead = true });

        Assert.Equal(1, mail.AttachmentsCount);
        Assert.False(mail.AttachmentsPending);
    }
}

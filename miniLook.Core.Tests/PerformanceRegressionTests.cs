using System.Text.Json;
using miniLook.Core.Helpers;
using miniLook.Models;

namespace miniLook.Core.Tests;

public class PerformanceRegressionTests
{
    [Theory]
    [InlineData("sender+tag@example.com")]
    [InlineData("sender@example.technology")]
    [InlineData(" sender@example.com ")]
    public void ValidMailboxAddressesAreAccepted(string address) =>
        Assert.True(EmailValidation.IsValidAddress(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Display Name (sender@example.com)")]
    [InlineData("Display Name <sender@example.com>")]
    [InlineData("sender@example.com\r\nBcc: other@example.com")]
    [InlineData("not-an-address")]
    public void DisplayStringsAndInvalidRecipientsAreRejected(string? address) =>
        Assert.False(EmailValidation.IsValidAddress(address));

    [Fact]
    public void PollingBacksOffAndUsesSlowerHiddenRefreshes()
    {
        Assert.True(PollingPolicy.GetInterval(false, 0) > PollingPolicy.GetInterval(true, 0));
        Assert.True(PollingPolicy.GetInterval(true, 2) > PollingPolicy.GetInterval(true, 1));
        Assert.Equal(PollingPolicy.GetInterval(false, 100), PollingPolicy.GetInterval(false, int.MaxValue));
        Assert.True(PollingPolicy.GetCalendarInterval(false) > PollingPolicy.GetCalendarInterval(true));
    }

    [Fact]
    public void UnchangedGroupsDoNotNotifyOrReplaceMessages()
    {
        MailData mail = CreateMail("first", "subject");
        ConversationGroup group = new("conversation", [mail]);
        List<string?> notifications = [];
        int collectionChanges = 0;
        group.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        group.Messages.CollectionChanged += (_, _) => collectionChanges++;

        group.SyncMessages([mail]);

        Assert.Empty(notifications);
        Assert.Equal(0, collectionChanges);
        Assert.Same(mail, group.LatestMessage);
    }

    [Fact]
    public void NewConversationMessageNotifiesChangedSummariesAndPreservesExpansion()
    {
        MailData first = CreateMail("first", "old");
        MailData latest = CreateMail("latest", "new");
        ConversationGroup group = new("conversation", [first]) { IsExpanded = true };
        List<string?> notifications = [];
        group.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        group.SyncMessages([latest, first]);

        Assert.True(group.IsExpanded);
        Assert.Same(first, group.Messages[1]);
        Assert.Same(latest, group.LatestMessage);
        Assert.Contains(nameof(ConversationGroup.Subject), notifications);
        Assert.Contains(nameof(ConversationGroup.MessageCount), notifications);
        Assert.Contains(nameof(ConversationGroup.HasMultipleMessages), notifications);
    }

    [Fact]
    public void MetadataChangesAreNotifiedWithoutReplacingTheMailObject()
    {
        MailData mail = CreateMail("first", "old");
        ConversationGroup group = new("conversation", [mail]);
        List<string?> notifications = [];
        group.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.True(mail.UpdateMetadata(CreateMail("first", "new"), false));
        group.SyncMessages([mail]);

        Assert.Same(mail, group.LatestMessage);
        Assert.Contains(nameof(ConversationGroup.Subject), notifications);
        Assert.DoesNotContain(nameof(ConversationGroup.LatestMessage), notifications);
    }

    [Fact]
    public void ReadOnlyChangesDoNotReNotifyUnchangedMetadata()
    {
        MailData mail = CreateMail("first", "subject");
        MailData updated = CreateMail("first", "subject");
        updated.IsRead = true;
        List<string?> notifications = [];
        mail.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        Assert.True(mail.UpdateMetadata(updated, null));

        Assert.Equal([nameof(MailData.IsRead)], notifications);
    }

    [Fact]
    public void PartialDeltaWithReceivedDatePreservesMissingMetadataAndFlags()
    {
        MailData mail = CreateMail("first", "keep-subject");
        mail.IsFocused = false;
        string sender = mail.Sender;

        Assert.True(mail.ApplyDelta(new Microsoft.Graph.Models.Message
        {
            Id = mail.Id,
            ReceivedDateTime = DateTimeOffset.UtcNow,
            IsRead = true
        }));

        Assert.Equal("keep-subject", mail.Subject);
        Assert.Equal(sender, mail.Sender);
        Assert.False(mail.IsFocused);
        Assert.True(mail.IsRead);
    }

    [Fact]
    public void CacheSnapshotIsDetachedAndDoesNotPersistDownloadedAttachmentState()
    {
        MailData mail = CreateMail("first", "subject");
        mail.Attachments.Add(new AttachmentInfo { Id = "attachment", Name = "original.pdf", LocalFilePath = "cached-file" });
        List<MailData> snapshot = [mail.CreateCacheSnapshot()];
        mail.IsRead = true;
        mail.Attachments[0].Name = "changed.pdf";

        string json = JsonSerializer.Serialize(snapshot, MailCacheJsonContext.Default.ListMailData);
        List<MailData> restored = JsonSerializer.Deserialize(json, MailCacheJsonContext.Default.ListMailData)!;

        Assert.False(restored[0].IsRead);
        Assert.Equal("original.pdf", restored[0].Attachments[0].Name);
        Assert.DoesNotContain(nameof(AttachmentInfo.LocalFilePath), json);
    }

    [Fact]
    public void AttachmentCacheNamesStayLocalAndSeparateSameNamedFiles()
    {
        string first = AttachmentFileName.Create("mail-one", "attachment", @"..\..\report.pdf");
        string second = AttachmentFileName.Create("mail-two", "attachment", "report.pdf");
        Assert.Equal(Path.GetFileName(first), first);
        Assert.EndsWith("-report.pdf", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void LongAttachmentNamesKeepTheirExtension()
    {
        string fileName = AttachmentFileName.Create("mail", "attachment", new string('a', 250) + ".pdf");
        Assert.EndsWith(".pdf", fileName);
        Assert.True(fileName.Length < 255);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCanceledCacheWritesPreserveTheLastGoodFile(bool cancel)
    {
        string directory = Path.Combine(Path.GetTempPath(), "miniLook-cache-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "cache.json");
        try
        {
            await File.WriteAllTextAsync(path, "last-good-cache");
            using CancellationTokenSource cancellation = new();
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.WriteAsync(path, async (stream, token) =>
                {
                    await stream.WriteAsync("partial"u8.ToArray(), token);
                    cancellation.Cancel();
                }, cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => AtomicFile.WriteAsync(path, async (stream, token) =>
                {
                    await stream.WriteAsync("partial"u8.ToArray(), token);
                    throw new IOException("Simulated write failure.");
                }));
            }
            Assert.Equal("last-good-cache", await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static MailData CreateMail(string id, string subject) => new()
    {
        Id = id,
        ConversationId = "conversation",
        Subject = subject,
        Sender = "Sender (sender@example.com)",
        SenderAddress = "sender@example.com"
    };
}
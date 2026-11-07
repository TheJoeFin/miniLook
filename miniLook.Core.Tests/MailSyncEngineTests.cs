using System.Collections.ObjectModel;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using miniLook.Contracts.Services;
using miniLook.Core.Helpers;
using miniLook.Models;
using miniLook.Services;
using static miniLook.Core.Tests.Mail;
using AttachmentInfo = miniLook.Models.AttachmentInfo;

namespace miniLook.Core.Tests;

public class MailSyncEngineTests
{
    private const int WindowMonths = 2;

    private readonly FakeMailCache _cache = new();
    private readonly FakeMailSyncClient _client = new();
    private readonly MailSyncEngine _engine;

    public MailSyncEngineTests()
    {
        _engine = new MailSyncEngine(_cache);
    }

    private Task SyncAsync(IList<MailData> items, Action? onMailChanged = null, CancellationToken cancellationToken = default, int windowMonths = WindowMonths) =>
        _engine.SyncAsync(_client, items, windowMonths, Now, onMailChanged, cancellationToken);

    private static MailSyncCursorExpiredException Expired() => new(new InvalidOperationException("expired"));

    // ---------------- First big sync ----------------

    [Fact]
    public async Task FirstSyncWalksEveryPageAndKeepsMailNewestFirst()
    {
        _client
            .On(FakeMailSyncClient.Initial, Next("page2", Full("newest", 1), Full("old", 30)))
            .On("page2", Next("page3", Full("middle", 10)))
            .On("page3", Last("delta1", Full("oldest", 50)));
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal(["newest", "middle", "old", "oldest"], Ids(items));
        Assert.Equal([FakeMailSyncClient.Initial, "page2", "page3"], _client.Requests);
        Assert.Equal("delta1", _cache.DeltaLink);
    }

    [Fact]
    public async Task FirstSyncAsksForTheRollingWindow()
    {
        _client.On(FakeMailSyncClient.Initial, Last("delta1"));

        await SyncAsync([]);

        Assert.Equal(MailSyncPolicy.GetCutoff(Now, WindowMonths), Assert.Single(_client.Cutoffs));
    }

    [Fact]
    public async Task EmptyInboxStillFinishesWithACursor()
    {
        _client.On(FakeMailSyncClient.Initial, Last("delta1"));
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Empty(items);
        Assert.Equal("delta1", _cache.DeltaLink);
    }

    [Fact]
    public async Task AnEmptyPageWithANextLinkDoesNotEndTheRound()
    {
        _client
            .On(FakeMailSyncClient.Initial, Next("page2"))
            .On("page2", Last("delta1", Full("first")));
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal(["first"], Ids(items));
    }

    [Fact]
    public async Task EachChangedPageIsReportedSoALongSyncCanShowProgress()
    {
        _client
            .On(FakeMailSyncClient.Initial, Next("page2", Full("one")))
            .On("page2", Next("page3", Full("two")))
            .On("page3", Last("delta1", Full("three")));
        ObservableCollection<MailData> items = [];
        List<int> countsSeen = [];

        await SyncAsync(items, () => countsSeen.Add(items.Count));

        Assert.Equal([1, 2, 3], countsSeen);
    }

    [Fact]
    public async Task CanceledFirstSyncContinuesFromTheLastPageInsteadOfStartingOver()
    {
        using CancellationTokenSource cancellation = new();
        _client
            .On(FakeMailSyncClient.Initial, Next("page2", Full("one")))
            .On("page2", () =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }, () => Next("page3", Full("two")))
            .On("page3", Last("delta1", Full("three")));
        ObservableCollection<MailData> items = [];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SyncAsync(items, cancellationToken: cancellation.Token));
        Assert.Equal(["one"], Ids(items));
        Assert.Null(_cache.DeltaLink);

        _client.Requests.Clear();
        await SyncAsync(items);

        Assert.Equal(["page2", "page3"], _client.Requests);
        Assert.Equal(["one", "two", "three"], Ids(items));
        Assert.Equal("delta1", _cache.DeltaLink);
    }

    [Fact]
    public async Task AFailedPageIsRetriedFromThatPage()
    {
        _client
            .On(FakeMailSyncClient.Initial, Next("page2", Full("one")))
            .On("page2", () => throw new HttpRequestException("Wi-Fi dropped"), () => Last("delta1", Full("two")));
        ObservableCollection<MailData> items = [];

        await Assert.ThrowsAsync<HttpRequestException>(() => SyncAsync(items));
        _client.Requests.Clear();
        await SyncAsync(items);

        Assert.Equal(["page2"], _client.Requests);
        Assert.Equal(2, items.Count);
        Assert.Equal("delta1", _cache.DeltaLink);
    }

    [Fact]
    public async Task MailFromEarlierPagesSurvivesWhenAResumedFullListingCompletes()
    {
        _client
            .On(FakeMailSyncClient.Initial, Next("page2", Full("first-page")))
            .On("page2", () => throw new HttpRequestException("offline"), () => Last("delta1", Full("second-page")));
        ObservableCollection<MailData> items = [];

        await Assert.ThrowsAsync<HttpRequestException>(() => SyncAsync(items));
        await SyncAsync(items);

        Assert.Contains(items, mail => mail.Id == "first-page");
        Assert.Contains(items, mail => mail.Id == "second-page");
    }

    [Fact]
    public async Task AttachmentListingsDoNotSlowDownOrInterruptPaging()
    {
        List<string?> cursorWhenListed = [];
        _client
            .On(FakeMailSyncClient.Initial, Next("page2", Full("one", hasAttachments: true)))
            .On("page2", Last("delta1", Full("two", hasAttachments: true)));
        _client.Attachments = _ =>
        {
            cursorWhenListed.Add(_cache.DeltaLink);
            return [];
        };

        await SyncAsync([]);

        // Both listings ran only once the whole round was finished and saved.
        Assert.Equal(["delta1", "delta1"], cursorWhenListed);
    }

    [Fact]
    public async Task OneMessageGraphWillNotListDoesNotFailTheSyncOrHoldUpOthers()
    {
        _client.On(FakeMailSyncClient.Initial, Last("delta1", Full("poison", 1, hasAttachments: true), Full("healthy", 2, hasAttachments: true)));
        _client.Attachments = id => id == "poison"
            ? throw new ODataError { ResponseStatusCode = 500 }
            : [new FileAttachment { Id = "a", Name = "report.pdf" }];
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal("delta1", _cache.DeltaLink);
        MailData poison = items.Single(mail => mail.Id == "poison");
        MailData healthy = items.Single(mail => mail.Id == "healthy");
        Assert.True(poison.AttachmentsPending);
        Assert.True(poison.HasAttachments);
        Assert.False(healthy.AttachmentsPending);
        Assert.Equal("report.pdf", Assert.Single(healthy.Attachments).Name);
    }

    [Fact]
    public async Task AMessageThatCouldNotBeListedIsNotRetriedOnEveryPoll()
    {
        _client
            .On(FakeMailSyncClient.Initial, Last("delta1", Full("poison", hasAttachments: true)))
            .On("delta1", Last("delta1"));
        _client.Attachments = _ => throw new ODataError { ResponseStatusCode = 500 };
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);
        await SyncAsync(items);

        Assert.Single(_client.AttachmentRequests);
    }

    [Fact]
    public async Task ThrottlingStopsTheBatchWithoutGivingUpOnAnyMessage()
    {
        _client
            .On(FakeMailSyncClient.Initial, Last("delta1", Full("one", 1, hasAttachments: true), Full("two", 2, hasAttachments: true)))
            .On("delta1", Last("delta1"));
        _client.Attachments = _ => throw new ODataError { ResponseStatusCode = 429 };
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);
        Assert.Equal(["one"], _client.AttachmentRequests);

        _client.Attachments = _ => [];
        await SyncAsync(items);
        Assert.Equal(["one", "one", "two"], _client.AttachmentRequests);
        Assert.DoesNotContain(items, mail => mail.AttachmentsPending);
    }

    [Fact]
    public async Task ANetworkFailureDuringAttachmentListingsLeavesTheFinishedSyncIntact()
    {
        _client.On(FakeMailSyncClient.Initial, Last("delta1", Full("one", hasAttachments: true)));
        _client.Attachments = _ => throw new HttpRequestException("offline");
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal("delta1", _cache.DeltaLink);
        Assert.True(items[0].AttachmentsPending);
    }

    [Fact]
    public async Task AttachmentListingsAreBoundedPerPassAndNewestFirst()
    {
        Message[] messages = Enumerable.Range(0, MailSyncPolicy.AttachmentBatchSize + 5)
            .Select(i => Full($"m{i}", ageDays: i + 1, hasAttachments: true))
            .ToArray();
        _client
            .On(FakeMailSyncClient.Initial, Last("delta1", messages))
            .On("delta1", Last("delta1"));
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal(MailSyncPolicy.AttachmentBatchSize, _client.AttachmentRequests.Count);
        Assert.Equal("m0", _client.AttachmentRequests[0]);

        await SyncAsync(items);

        Assert.Equal(messages.Length, _client.AttachmentRequests.Count);
        Assert.DoesNotContain(items, mail => mail.AttachmentsPending);
    }

    [Fact]
    public async Task AttachmentListingsKeepOnlyOpenableFilesButCountEverything()
    {
        _client.On(FakeMailSyncClient.Initial, Last("delta1", Full("one", hasAttachments: true)));
        _client.Attachments = _ => [new FileAttachment { Id = "f", Name = "file.pdf" }, new ItemAttachment { Id = "i", Name = "forwarded" }];
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal(2, items[0].AttachmentsCount);
        Assert.Equal("f", Assert.Single(items[0].Attachments).Id);
    }

    [Fact]
    public async Task SignInExpiryIsNotMistakenForAnAttachmentProblem()
    {
        _client.On(FakeMailSyncClient.Initial, Last("delta1", Full("one", hasAttachments: true)));
        _client.Attachments = _ => throw new SignInRequiredException(new InvalidOperationException());

        await Assert.ThrowsAsync<SignInRequiredException>(() => SyncAsync([]));
    }

    // ---------------- Everyday diff sync ----------------

    [Fact]
    public async Task DiffSyncUsesTheSavedCursorAndKeepsMailItWasNotToldAbout()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", Full("new", 0)));
        ObservableCollection<MailData> items = Local(Full("older", 5));

        await SyncAsync(items);

        Assert.Equal(["new", "older"], Ids(items));
        Assert.Equal(["delta1"], _client.Requests);
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task RemovedMailLeavesTheList()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", Removed("gone")));
        ObservableCollection<MailData> items = Local(Full("gone", 1), Full("kept", 2));

        await SyncAsync(items);

        Assert.Equal(["kept"], Ids(items));
        Assert.Equal(["kept"], _cache.SavedIds);
    }

    [Fact]
    public async Task APartialUpdateChangesOnlyWhatItCarriesOnTheSameObject()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", Partial("one", isRead: true, classification: InferenceClassificationType.Other)));
        ObservableCollection<MailData> items = Local(Full("one"));
        MailData original = items[0];
        string subject = original.Subject;

        await SyncAsync(items);

        MailData mail = Assert.Single(items);
        Assert.Same(original, mail);
        Assert.True(mail.IsRead);
        Assert.False(mail.IsFocused);
        Assert.Equal(subject, mail.Subject);
        Assert.Equal(Now.AddDays(-1), mail.ReceivedDateTime);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadStateChangesOnMailOutsideTheWindowAreNotAdded(bool complete)
    {
        // Graph documents that delta can report read-state changes that do not match the filter.
        _cache.DeltaLink = "delta1";
        Message change = complete ? Full("ancient", ageDays: 800, isRead: true) : Partial("ancient", isRead: true);
        _client.On("delta1", Last("delta2", change));
        ObservableCollection<MailData> items = Local(Full("recent"));

        await SyncAsync(items);

        Assert.Equal(["recent"], Ids(items));
        Assert.Equal(0, _cache.MailSaves);
    }

    [Fact]
    public async Task ReplayedChangesAreSafe()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", Full("new", 0), Full("new", 0), Removed("missing"), Removed("missing")));
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal(["new"], Ids(items));
    }

    [Fact]
    public async Task MailRemovedThenRestoredInOneRoundEndsUpPresent()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", Removed("back"), Full("back")));
        ObservableCollection<MailData> items = Local(Full("back"));

        await SyncAsync(items);

        Assert.Equal(["back"], Ids(items));
    }

    [Fact]
    public async Task EntriesWithoutAnIdAreSkippedRatherThanBlockingTheCursorForever()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", new Message { Subject = "no id" }, Full("real")));
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal(["real"], Ids(items));
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task AQuietPollSavesTheCursorWithoutRewritingTheCache()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2"));
        ObservableCollection<MailData> items = Local(Full("one"));
        int changes = 0;

        await SyncAsync(items, () => changes++);

        Assert.Equal(0, _cache.MailSaves);
        Assert.Equal(0, changes);
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task LocalEditsAreSavedOnTheNextPollEvenWhenTheServerHasNoChanges()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta1"));
        ObservableCollection<MailData> items = Local(Full("one"));

        _engine.MarkCacheDirty();
        await SyncAsync(items);

        Assert.Equal(1, _cache.MailSaves);
    }

    [Fact]
    public async Task TheCacheIsWrittenBeforeTheCursorSoACrashOnlyReplaysChanges()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", Full("new")));

        await SyncAsync([]);

        Assert.Equal(["mail", "cursor:delta2"], _cache.Writes);
    }

    [Fact]
    public async Task AFailedCacheWriteKeepsTheOldCursorAndTriesAgain()
    {
        _cache.DeltaLink = "delta1";
        _cache.NextMailSaveFailure = new IOException("disk full");
        _client.On("delta1", Last("delta2", Full("new")));
        ObservableCollection<MailData> items = [];

        await Assert.ThrowsAsync<IOException>(() => SyncAsync(items));
        Assert.Equal("delta1", _cache.DeltaLink);

        await SyncAsync(items);

        Assert.Equal("delta2", _cache.DeltaLink);
        Assert.Equal(["new"], _cache.SavedIds);
    }

    [Fact]
    public async Task AttachmentsAddedToKnownMailLaterAreStillFound()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2", Full("one", hasAttachments: true)));
        ObservableCollection<MailData> items = Local(Full("one", hasAttachments: false));

        await SyncAsync(items);

        Assert.Equal(["one"], _client.AttachmentRequests);
        Assert.Equal(1, items[0].AttachmentsCount);
    }

    [Fact]
    public async Task APageWithNoLinksFailsInsteadOfPretendingToFinish()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", new MailDeltaPage([], null, null));

        await Assert.ThrowsAsync<InvalidDataException>(() => SyncAsync([]));

        Assert.Equal("delta1", _cache.DeltaLink);
    }

    // ---------------- Away for a long time ----------------

    [Fact]
    public async Task AnExpiredCursorFallsBackToAFullListing()
    {
        _cache.DeltaLink = "stale";
        _client
            .On("stale", () => throw Expired())
            .On(FakeMailSyncClient.Initial, Last("delta2", Full("current")));
        ObservableCollection<MailData> items = [];

        await SyncAsync(items);

        Assert.Equal(["current"], Ids(items));
        Assert.Equal(["stale", FakeMailSyncClient.Initial], _client.Requests);
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task AResyncRemovesMailDeletedWhileAway()
    {
        // A full listing has no @removed entries for what vanished before it started.
        _cache.DeltaLink = "stale";
        _client
            .On("stale", () => throw Expired())
            .On(FakeMailSyncClient.Initial, Next("page2", Full("still-here", 3)))
            .On("page2", Last("delta2", Full("arrived-while-away", 1)));
        ObservableCollection<MailData> items = Local(Full("still-here", 3), Full("deleted-while-away", 4));

        await SyncAsync(items);

        Assert.Equal(["arrived-while-away", "still-here"], Ids(items));
        Assert.Equal(["arrived-while-away", "still-here"], _cache.SavedIds);
    }

    [Fact]
    public async Task AResyncUpdatesKnownMailInPlaceAndRefreshesItsReadState()
    {
        _cache.DeltaLink = "stale";
        _client
            .On("stale", () => throw Expired())
            .On(FakeMailSyncClient.Initial, Last("delta2", Full("one", isRead: true)));
        ObservableCollection<MailData> items = Local(Full("one", isRead: false));
        MailData original = items[0];

        await SyncAsync(items);

        Assert.Same(original, Assert.Single(items));
        Assert.True(original.IsRead);
    }

    [Fact]
    public async Task AResyncDoesNotRefetchAttachmentsItAlreadyHas()
    {
        _cache.DeltaLink = "stale";
        _client
            .On("stale", () => throw Expired())
            .On(FakeMailSyncClient.Initial, Last("delta2", Full("one", hasAttachments: true)));
        ObservableCollection<MailData> items = Local(Full("one"));
        items[0].SetAttachments([new AttachmentInfo { Id = "a", Name = "kept.pdf" }], 1);

        await SyncAsync(items);

        Assert.Empty(_client.AttachmentRequests);
        Assert.Equal("kept.pdf", Assert.Single(items[0].Attachments).Name);
    }

    [Fact]
    public async Task AnExpiredNextLinkPartWayThroughADiffRestartsAsAFullListing()
    {
        _cache.DeltaLink = "delta1";
        _client
            .On("delta1", Next("page2", Full("new")))
            .On("page2", () => throw Expired())
            .On(FakeMailSyncClient.Initial, Last("delta2", Full("new"), Full("older", 5)));
        ObservableCollection<MailData> items = Local(Full("older", 5), Full("ghost", 6));

        await SyncAsync(items);

        Assert.Equal(["new", "older"], Ids(items));
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task AnInterruptedResyncDoesNotGoBackToTheExpiredCursor()
    {
        _cache.DeltaLink = "stale";
        _client
            .On("stale", () => throw Expired())
            .On(FakeMailSyncClient.Initial, () => throw new HttpRequestException("offline"), () => Last("delta2", Full("current")));
        ObservableCollection<MailData> items = [];

        await Assert.ThrowsAsync<HttpRequestException>(() => SyncAsync(items));
        Assert.Null(_cache.DeltaLink);

        _client.Requests.Clear();
        await SyncAsync(items);

        Assert.Equal([FakeMailSyncClient.Initial], _client.Requests);
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task AppRestartDuringAResyncStillListsEverything()
    {
        _cache.DeltaLink = "stale";
        _client
            .On("stale", () => throw Expired())
            .On(FakeMailSyncClient.Initial, () => throw new HttpRequestException("offline"), () => Last("delta2", Full("current")));

        await Assert.ThrowsAsync<HttpRequestException>(() => SyncAsync([]));

        MailSyncEngine afterRestart = new(_cache);
        ObservableCollection<MailData> items = Local(Full("ghost", 3));
        await afterRestart.SyncAsync(_client, items, WindowMonths, Now, null, CancellationToken.None);

        Assert.Equal(["current"], Ids(items));
    }

    [Fact]
    public async Task ACursorThatKeepsExpiringFailsInsteadOfLoopingForever()
    {
        _cache.DeltaLink = "stale";
        _client
            .On("stale", () => throw Expired())
            .On(FakeMailSyncClient.Initial, Next("page2", Full("one")))
            .On("page2", () => throw Expired());

        await Assert.ThrowsAsync<MailSyncCursorExpiredException>(() => SyncAsync([]));

        Assert.Equal(["stale", FakeMailSyncClient.Initial, "page2"], _client.Requests);
    }

    [Fact]
    public async Task AnOutageDoesNotThrowAwayAGoodCursor()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", () => throw new ODataError { ResponseStatusCode = 503 }, () => Last("delta2"));

        await Assert.ThrowsAsync<ODataError>(() => SyncAsync([]));
        Assert.Equal("delta1", _cache.DeltaLink);

        await SyncAsync([]);
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task ALongDiffAfterAnAbsenceResumesFromItsLastPage()
    {
        _cache.DeltaLink = "delta1";
        _client
            .On("delta1", Next("page2", Removed("gone")))
            .On("page2", () => throw new HttpRequestException("offline"), () => Last("delta2", Full("new", 0)));
        ObservableCollection<MailData> items = Local(Full("gone", 1), Full("kept", 2));

        await Assert.ThrowsAsync<HttpRequestException>(() => SyncAsync(items));
        _client.Requests.Clear();
        await SyncAsync(items);

        Assert.Equal(["page2"], _client.Requests);
        Assert.Equal(["new", "kept"], Ids(items));
        Assert.Equal(["new", "kept"], _cache.SavedIds);
    }

    [Fact]
    public async Task MailAgesOutOfTheWindowEvenThoughTheDeltaFilterIsFrozen()
    {
        _cache.DeltaLink = "delta1";
        _client.On("delta1", Last("delta2"));
        ObservableCollection<MailData> items = Local(Full("recent", 5), Full("aged-out", 90));

        await SyncAsync(items);

        Assert.Equal(["recent"], Ids(items));
        Assert.Equal(["recent"], _cache.SavedIds);
    }

    // ---------------- Load older mail ----------------

    [Fact]
    public async Task WideningTheWindowForgetsTheCursorBeforeAnyMailIsListed()
    {
        // Otherwise an app restart mid-way would keep the old cursor and never fetch the older mail.
        _cache.DeltaLink = "delta1";

        await _engine.RequestFullEnumerationAsync();

        Assert.Null(_cache.DeltaLink);
    }

    [Fact]
    public async Task WideningTheWindowAddsOlderMailAndLosesNothing()
    {
        _cache.DeltaLink = "delta1";
        _client.On(FakeMailSyncClient.Initial, Last("delta2", Full("recent", 5), Full("from-extra-month", 70)));
        ObservableCollection<MailData> items = Local(Full("recent", 5));

        await _engine.RequestFullEnumerationAsync();
        await SyncAsync(items, windowMonths: 3);

        Assert.Equal(["recent", "from-extra-month"], Ids(items));
        Assert.Equal(MailSyncPolicy.GetCutoff(Now, 3), Assert.Single(_client.Cutoffs));
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    [Fact]
    public async Task WideningTheWindowDuringASyncAbandonsThePartialRound()
    {
        _client
            .On(FakeMailSyncClient.Initial, () => Next("page2", Full("one")), () => Last("delta2", Full("one"), Full("older", 70)))
            .On("page2", () => throw new HttpRequestException("offline"));
        ObservableCollection<MailData> items = [];

        await Assert.ThrowsAsync<HttpRequestException>(() => SyncAsync(items));
        await _engine.RequestFullEnumerationAsync();
        await SyncAsync(items, windowMonths: 3);

        Assert.Equal(["one", "older"], Ids(items));
        Assert.Equal("delta2", _cache.DeltaLink);
    }

    // ---------------- Resetting and loading the cache ----------------

    [Fact]
    public async Task ResetForgetsAPartialRoundSoAnotherAccountStartsClean()
    {
        _client
            .On(FakeMailSyncClient.Initial, () => Next("page2", Full("first-account")), () => Last("delta2", Full("second-account")))
            .On("page2", () => throw new HttpRequestException("offline"));

        await Assert.ThrowsAsync<HttpRequestException>(() => SyncAsync([]));
        _engine.Reset();
        ObservableCollection<MailData> items = [];
        _client.Requests.Clear();
        await SyncAsync(items);

        Assert.Equal([FakeMailSyncClient.Initial], _client.Requests);
        Assert.Equal(["second-account"], Ids(items));
    }

    [Fact]
    public void ACacheWithDuplicatesOrBlankIdsCannotBreakTheSync()
    {
        // Building the ID lookup used to throw on a duplicate, failing every future sync.
        MailData newer = new() { Id = "newer", ReceivedDateTime = Now };
        MailData older = new() { Id = "older", ReceivedDateTime = Now.AddDays(-5) };
        MailData duplicate = new() { Id = "older", ReceivedDateTime = Now.AddDays(-5) };
        MailData blank = new() { Id = string.Empty, ReceivedDateTime = Now };

        List<MailData> normalized = MailSyncEngine.NormalizeCached([older, blank, newer, duplicate]);

        Assert.Equal(["newer", "older"], Ids(normalized));
        Assert.Same(older, normalized[1]);
    }

    [Fact]
    public void InsertingKeepsTheListNewestFirstAndEqualTimesInArrivalOrder()
    {
        List<MailData> items = [];
        foreach (string id in new[] { "c", "a", "b" })
        {
            int age = id switch { "a" => 1, "b" => 2, _ => 3 };
            MailSyncEngine.InsertNewestFirst(items, new MailData { Id = id, ReceivedDateTime = Now.AddDays(-age) });
        }
        MailSyncEngine.InsertNewestFirst(items, new MailData { Id = "b2", ReceivedDateTime = Now.AddDays(-2) });

        Assert.Equal(["a", "b", "b2", "c"], Ids(items));
    }
}

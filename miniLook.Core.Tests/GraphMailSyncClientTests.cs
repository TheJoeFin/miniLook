using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Authentication;
using miniLook.Contracts.Services;
using miniLook.Core.Helpers;
using miniLook.Models;
using miniLook.Services;

namespace miniLook.Core.Tests;

/// <summary>
/// Runs the real Graph SDK against canned HTTP responses shaped like the documented ones, to prove what
/// the fakes in the engine tests assume: how removals look, how errors surface, and what is sent.
/// </summary>
public class GraphMailSyncClientTests
{
    private const string NextLink = "https://graph.microsoft.com/v1.0/me/mailFolders('Inbox')/messages/delta?$skiptoken=page2";
    private const string DeltaLink = "https://graph.microsoft.com/v1.0/me/mailFolders('Inbox')/messages/delta?$deltatoken=cursor";
    private const string EmptyLastPage = $$"""{ "@odata.deltaLink": "{{DeltaLink}}", "value": [] }""";

    private readonly CannedHandler _handler = new();
    private readonly GraphMailSyncClient _client;

    public GraphMailSyncClientTests()
    {
        _client = new GraphMailSyncClient(new GraphServiceClient(new HttpClient(_handler), new AnonymousAuthenticationProvider()));
    }

    [Fact]
    public async Task TheFirstRequestAsksForTheNewestMailInTheWindow()
    {
        _handler.Reply(HttpStatusCode.OK, EmptyLastPage);

        await _client.GetInitialPageAsync(new DateTimeOffset(2026, 9, 6, 7, 5, 9, TimeSpan.Zero), CancellationToken.None);

        HttpRequestMessage request = Assert.Single(_handler.Requests);
        string url = Uri.UnescapeDataString(request.RequestUri!.ToString()).Replace('+', ' ');
        Assert.Contains("/me/mailFolders/Inbox/messages/delta", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$filter=receivedDateTime ge 2026-09-06T07:05:09Z", url);
        Assert.Contains("$orderby=receivedDateTime desc", url);
        Assert.Contains("hasAttachments", url);
        Assert.Contains("inferenceClassification", url);
        Assert.Equal($"odata.maxpagesize={MailSyncPolicy.DeltaPageSize}", Assert.Single(request.Headers.GetValues("Prefer")));
    }

    [Fact]
    public async Task LinksAreFollowedExactlyAsIssuedAndKeepThePageSize()
    {
        _handler.Reply(HttpStatusCode.OK, EmptyLastPage);

        MailDeltaPage page = await _client.GetLinkedPageAsync(NextLink, CancellationToken.None);

        HttpRequestMessage request = Assert.Single(_handler.Requests);
        Assert.Contains("$skiptoken=page2", Uri.UnescapeDataString(request.RequestUri!.ToString()));
        Assert.DoesNotContain("$filter", request.RequestUri!.ToString());
        Assert.Equal($"odata.maxpagesize={MailSyncPolicy.DeltaPageSize}", Assert.Single(request.Headers.GetValues("Prefer")));
        Assert.Equal(DeltaLink, page.DeltaLink);
        Assert.Null(page.NextLink);
    }

    [Fact]
    public async Task ADocumentedDeltaPageParsesIntoWhatTheEngineExpects()
    {
        _handler.Reply(HttpStatusCode.OK, $$"""
            {
              "@odata.nextLink": "{{NextLink}}",
              "value": [
                {
                  "@odata.type": "#microsoft.graph.message",
                  "id": "removed-id",
                  "@removed": { "reason": "deleted" }
                },
                {
                  "@odata.type": "#microsoft.graph.message",
                  "id": "partial-id",
                  "isRead": true
                },
                {
                  "@odata.type": "#microsoft.graph.eventMessageRequest",
                  "id": "full-id",
                  "subject": "Holiday hours update",
                  "isRead": false,
                  "hasAttachments": true,
                  "conversationId": "conversation",
                  "inferenceClassification": "other",
                  "receivedDateTime": "2026-11-01T10:00:00Z",
                  "sender": { "emailAddress": { "name": "Dana Swope", "address": "danas@contoso.com" } }
                }
              ]
            }
            """);

        MailDeltaPage page = await _client.GetLinkedPageAsync(DeltaLink, CancellationToken.None);

        Assert.Equal(NextLink, page.NextLink);
        Assert.Null(page.DeltaLink);
        Assert.Equal(3, page.Messages.Count);

        Assert.True(page.Messages[0].AdditionalData.ContainsKey("@removed"));

        Assert.False(page.Messages[1].AdditionalData.ContainsKey("@removed"));
        Assert.True(page.Messages[1].IsRead);
        Assert.Null(page.Messages[1].ReceivedDateTime);

        MailData full = new(page.Messages[2]);
        Assert.Equal("Dana Swope", full.SenderName);
        Assert.True(full.IsEvent);
        Assert.False(full.IsFocused);
        Assert.True(full.AttachmentsPending);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), full.ReceivedDateTime);
    }

    [Fact]
    public async Task ThePagesOfARealResponseDriveTheEngineFromEndToEnd()
    {
        // The cursor has expired, and "deleted-while-away" is only in the local cache.
        _handler
            .Reply(HttpStatusCode.Gone, string.Empty)
            .Reply(HttpStatusCode.OK, $$"""
                {
                  "@odata.nextLink": "{{NextLink}}",
                  "value": [ { "id": "kept", "subject": "Kept", "isRead": true, "receivedDateTime": "2026-11-05T10:00:00Z" } ]
                }
                """)
            .Reply(HttpStatusCode.OK, $$"""
                {
                  "@odata.deltaLink": "{{DeltaLink}}",
                  "value": [ { "id": "new", "subject": "New", "receivedDateTime": "2026-11-06T10:00:00Z" } ]
                }
                """);
        FakeMailCache cache = new() { DeltaLink = "https://graph.microsoft.com/v1.0/me/mailFolders('Inbox')/messages/delta?$deltatoken=expired" };
        ObservableCollection<MailData> items = Mail.Local(Mail.Full("kept", 1), Mail.Full("deleted-while-away", 2));

        await new MailSyncEngine(cache).SyncAsync(_client, items, 2, Mail.Now, null, CancellationToken.None);

        Assert.Equal(["new", "kept"], Mail.Ids(items));
        Assert.True(items[1].IsRead);
        Assert.Equal(DeltaLink, cache.DeltaLink);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Fact]
    public async Task ASynchronizationResetWithNoBodyIsRecognized()
    {
        _handler.Reply(HttpStatusCode.Gone, string.Empty);

        await Assert.ThrowsAsync<MailSyncCursorExpiredException>(() => _client.GetLinkedPageAsync(DeltaLink, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "syncStateNotFound")]
    [InlineData(HttpStatusCode.NotFound, "SyncStateNotFound")]
    [InlineData(HttpStatusCode.Gone, "resyncRequired")]
    public async Task ExpiredTokenErrorsAreRecognized(HttpStatusCode status, string code)
    {
        _handler.Reply(status, ErrorBody(code));

        await Assert.ThrowsAsync<MailSyncCursorExpiredException>(() => _client.GetLinkedPageAsync(DeltaLink, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable")]
    [InlineData(HttpStatusCode.TooManyRequests, "TooManyRequests")]
    [InlineData(HttpStatusCode.Unauthorized, "InvalidAuthenticationToken")]
    public async Task OtherFailuresAreNotMistakenForExpiry(HttpStatusCode status, string code)
    {
        _handler.Reply(status, ErrorBody(code));

        ODataError error = await Assert.ThrowsAsync<ODataError>(() => _client.GetLinkedPageAsync(DeltaLink, CancellationToken.None));

        Assert.Equal((int)status, error.ResponseStatusCode);
        Assert.Equal(code, error.Error?.Code);
    }

    [Fact]
    public async Task AFirstRequestThatIsRejectedStaysAnError()
    {
        // Falling back to a full listing cannot help when the full listing itself is rejected.
        _handler.Reply(HttpStatusCode.BadRequest, ErrorBody("BadRequest"));

        await Assert.ThrowsAsync<ODataError>(() => _client.GetInitialPageAsync(Mail.Now, CancellationToken.None));
    }

    [Fact]
    public async Task AResponseWithoutAMessageCollectionIsRejected()
    {
        _handler.Reply(HttpStatusCode.OK, $$"""{ "@odata.deltaLink": "{{DeltaLink}}" }""");

        await Assert.ThrowsAsync<InvalidDataException>(() => _client.GetLinkedPageAsync(DeltaLink, CancellationToken.None));
    }

    [Fact]
    public async Task AttachmentListingsRequestMetadataOnlyAndParseFileAttachments()
    {
        _handler.Reply(HttpStatusCode.OK, """
            {
              "value": [
                { "@odata.type": "#microsoft.graph.fileAttachment", "id": "f", "name": "report.pdf", "contentType": "application/pdf" },
                { "@odata.type": "#microsoft.graph.itemAttachment", "id": "i", "name": "forwarded" }
              ]
            }
            """);

        IReadOnlyList<Attachment> attachments = await _client.GetAttachmentsAsync("message-id", CancellationToken.None);

        string url = Uri.UnescapeDataString(Assert.Single(_handler.Requests).RequestUri!.ToString());
        Assert.Contains("$select=id,name,contentType", url);
        Assert.IsType<FileAttachment>(attachments[0]);
        Assert.IsType<ItemAttachment>(attachments[1]);
    }

    [Fact]
    public async Task AnExpiredSignInReachesTheSyncUnwrappedAndSendsNoRequest()
    {
        // The view model tells "sign in again" apart from a failed refresh by this exact type.
        SignInRequiredException expired = new(new InvalidOperationException("ui required"));
        GraphServiceClient graph = new(
            new HttpClient(_handler),
            new BaseBearerTokenAuthenticationProvider(new ThrowingTokenProvider(expired)));

        SignInRequiredException thrown = await Assert.ThrowsAsync<SignInRequiredException>(
            () => new GraphMailSyncClient(graph).GetLinkedPageAsync(DeltaLink, CancellationToken.None));

        Assert.Same(expired, thrown);
        Assert.Empty(_handler.Requests);
    }

    private static string ErrorBody(string code) => $$"""{ "error": { "code": "{{code}}", "message": "Simulated failure." } }""";

    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public CannedHandler Reply(HttpStatusCode status, string body)
        {
            _replies.Enqueue((status, body));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            (HttpStatusCode status, string body) = _replies.Dequeue();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingTokenProvider(Exception exception) : IAccessTokenProvider
    {
        public AllowedHostsValidator AllowedHostsValidator { get; } = new();

        public Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default) =>
            throw exception;
    }
}

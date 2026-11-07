using Microsoft.Graph;
using Microsoft.Graph.Me.MailFolders.Item.Messages.Delta;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using miniLook.Contracts.Services;
using miniLook.Core.Helpers;

namespace miniLook.Services;

public sealed class GraphMailSyncClient : IMailSyncClient
{
    private const string PreferHeaderName = "Prefer";

    private static readonly string PreferredPageSize = $"odata.maxpagesize={MailSyncPolicy.DeltaPageSize}";

    private static readonly string[] MessageFields =
        ["id", "isRead", "sender", "toRecipients", "ccRecipients", "subject", "bodyPreview", "webLink", "receivedDateTime", "conversationId", "inferenceClassification", "hasAttachments"];

    private readonly GraphServiceClient _client;

    public GraphMailSyncClient(GraphServiceClient client)
    {
        _client = client;
    }

    public async Task<MailDeltaPage> GetInitialPageAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        DeltaGetResponse? response = await _client.Me.MailFolders["Inbox"].Messages.Delta.GetAsDeltaGetResponseAsync(config =>
        {
            config.QueryParameters.Select = MessageFields;
            config.QueryParameters.Filter = MailSyncPolicy.ReceivedOnOrAfter(cutoff);
            // A filtered delta stops at 5,000 messages and is otherwise unordered,
            // so without this a large mailbox could miss its newest mail.
            config.QueryParameters.Orderby = ["receivedDateTime desc"];
            config.Headers.Add(PreferHeaderName, PreferredPageSize);
        }, cancellationToken: cancellationToken);

        return ToPage(response);
    }

    public async Task<MailDeltaPage> GetLinkedPageAsync(string link, CancellationToken cancellationToken)
    {
        try
        {
            DeltaGetResponse? response = await new DeltaRequestBuilder(link, _client.RequestAdapter).GetAsDeltaGetResponseAsync(
                config => config.Headers.Add(PreferHeaderName, PreferredPageSize),
                cancellationToken: cancellationToken);

            return ToPage(response);
        }
        catch (ApiException ex) when (MailSyncPolicy.IsCursorInvalid(ex.ResponseStatusCode, (ex as ODataError)?.Error?.Code))
        {
            throw new MailSyncCursorExpiredException(ex);
        }
    }

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(string messageId, CancellationToken cancellationToken)
    {
        AttachmentCollectionResponse? response = await _client.Me.Messages[messageId].Attachments.GetAsync(config =>
        {
            config.QueryParameters.Select = ["id", "name", "contentType"];
        }, cancellationToken: cancellationToken);

        return response?.Value ?? throw new InvalidDataException("Graph returned no attachment collection.");
    }

    private static MailDeltaPage ToPage(DeltaGetResponse? response)
    {
        if (response?.Value is null)
            throw new InvalidDataException("Graph returned a delta page without a message collection.");

        return new MailDeltaPage(response.Value, response.OdataNextLink, response.OdataDeltaLink);
    }
}

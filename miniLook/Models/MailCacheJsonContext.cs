using System.Text.Json.Serialization;

namespace miniLook.Models;

[JsonSerializable(typeof(List<MailData>))]
internal partial class MailCacheJsonContext : JsonSerializerContext
{
}
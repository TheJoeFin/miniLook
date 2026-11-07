using System.Text.Json.Serialization;

namespace miniLook.Models;

[JsonSerializable(typeof(List<CalendarEventItem>))]
internal partial class CalendarCacheJsonContext : JsonSerializerContext
{
}

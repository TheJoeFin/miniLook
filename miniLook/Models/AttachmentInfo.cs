using System.Text.Json.Serialization;

namespace miniLook.Models;

public class AttachmentInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    [JsonIgnore]
    public string? LocalFilePath { get; set; }
}
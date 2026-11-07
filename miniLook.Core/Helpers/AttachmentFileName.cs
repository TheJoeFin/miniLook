using System.Security.Cryptography;
using System.Text;

namespace miniLook.Core.Helpers;

public static class AttachmentFileName
{
    public static string Create(string messageId, string attachmentId, string name)
    {
        string fileName = Path.GetFileName(name);
        foreach (char invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');
        fileName = fileName.TrimEnd(' ', '.');
        if (fileName.Length == 0)
            fileName = "attachment";
        if (fileName.Length > 180)
        {
            string extension = Path.GetExtension(fileName);
            if (extension.Length > 30)
                extension = string.Empty;
            fileName = fileName[..(180 - extension.Length)] + extension;
        }

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messageId + "\n" + attachmentId)))[..32];
        return hash + "-" + fileName;
    }
}
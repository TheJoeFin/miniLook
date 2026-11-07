#nullable enable
using System.Net.Mail;

namespace miniLook.Core.Helpers;

public static class EmailValidation
{
    public static bool IsValidAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n'))
            return false;

        string address = value.Trim();
        return MailAddress.TryCreate(address, out MailAddress? parsed)
            && parsed.DisplayName.Length == 0
            && string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase);
    }
}
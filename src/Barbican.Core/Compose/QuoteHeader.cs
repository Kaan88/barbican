// Mirrors: Rust compose.rs:136 format_recipient (Bulwark formatRecipient), :458-461 the reply header and the forwarded-message header ("On Tue 17 Nov 2026, 08:00, Ada wrote:", "---------- Forwarded message ----------");
// Bulwark lib/plugin-hook-catalog.ts:97 onBuildQuoteHeader.

using System.Globalization;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>The words above a quoted original and the recipient formatting of the composer's fields.</summary>
public static class QuoteHeader
{
    /// <summary>The first line of a forward.</summary>
    public const string ForwardedBanner = "---------- Forwarded message ----------";

    /// <summary>Bulwark formatRecipient: the bare address when there is no distinct name, otherwise <c>Name &lt;email&gt;</c>, quoting (and escaping) the name only when it holds a special character.</summary>
    public static string FormatRecipient(string? name, string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var n = (name ?? "").Trim();
        if (n.Length == 0 || string.Equals(n, email, StringComparison.OrdinalIgnoreCase))
        {
            return email;
        }

        return n.IndexOfAny([',', '<', '>', '"', '@', ';', ':']) >= 0
            ? $"\"{n.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\" <{email}>"
            : $"{n} <{email}>";
    }

    /// <summary>The sender as the header names them: the name alone when it is the address or there is none.</summary>
    public static string Who(EmailAddress? from)
    {
        if (from is null)
        {
            return "";
        }

        var name = string.IsNullOrWhiteSpace(from.Name) ? from.Email : from.Name;
        return string.Equals(name, from.Email, StringComparison.OrdinalIgnoreCase) || from.Email.Length == 0 ? name : $"{name} <{from.Email}>";
    }

    /// <summary>The date of the header, local: <c>Tue 17 Nov 2026, 08:00</c>.</summary>
    public static string Date(DateTimeOffset? at, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return at is { } t ? TimeZoneInfo.ConvertTime(t, zone).ToString("ddd dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) : "";
    }

    /// <summary>"On {date}, {who} wrote:".</summary>
    public static string Reply(string date, string who) => $"On {date}, {who} wrote:";
}

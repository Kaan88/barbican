// Mirrors: Bulwark components/email/search-result-count.tsx:14-31 (the line above search results: rows are conversations, so "10 results found" above three rows would
// read wrong), locales/en/common.json email_list.conversations_count_plus, mail_p3_list.conversations_found, advanced_search.results_found(_more), mail_list_extra.search_count.

using System.Globalization;

namespace Barbican.Presentation.Mail;

/// <summary>The words of the search results line.</summary>
public static class SearchResultCount
{
    /// <summary>The line for <paramref name="conversations"/> rows holding <paramref name="messages"/> messages; <paramref name="hasMore"/> while another page follows, <paramref name="threading"/> when the list groups conversations.</summary>
    public static string Text(int conversations, int messages, bool hasMore, bool threading)
    {
        if (hasMore)
        {
            return threading
                ? Num(conversations) + "+ conversations"
                : Num(messages) + "+ results found";
        }

        if (conversations == messages)
        {
            return threading
                ? conversations switch { 0 => "No results found", 1 => "1 conversation found", _ => Num(conversations) + " conversations found" }
                : messages switch { 0 => "No results found", 1 => "1 result found", _ => Num(messages) + " results found" };
        }

        return $"{Plural(conversations, "conversation")} ({Plural(messages, "message")})";
    }

    private static string Num(int n) => n.ToString(CultureInfo.CurrentCulture);

    private static string Plural(int n, string noun) => n == 1 ? "1 " + noun : Num(n) + " " + noun + "s";
}

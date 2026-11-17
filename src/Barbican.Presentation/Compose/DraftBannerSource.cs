// Mirrors: Bulwark components/email/email-viewer.tsx:6639 (the reply draft banner: Edit and Discard), thread-conversation-view.tsx:584,598 (the draft card), mail-app.tsx:6979 (continue draft banner + discard); I-218, I-224, I-281.
// A message in Drafts shows a banner above its body; Edit opens it in the composer (the seeder reopens it with its inline images), Discard deletes the draft. Owner composer-ui.

using System.Runtime.CompilerServices;
using Barbican.Core.Compose;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Compose;

/// <summary>The "this is a draft" banner and its two buttons.</summary>
public sealed class DraftBannerSource(IComposerService composer, IDraftService drafts, IActiveMail active, IToastService? toasts = null) : IReaderBannerSource, IBannerActionHandler
{
    public const string Id = "draft";
    public const string Edit = "draft.edit";
    public const string Discard = "draft.discard";

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await Task.CompletedTask.ConfigureAwait(false);
        if (snapshot.Body is { } body && body.Keywords.IsSet("$draft"))
        {
            yield return new ReaderBanner(Id, "This message is a draft.", "It was not sent yet.", false, [(Edit, "Edit"), (Discard, "Discard")]);
        }
    }

    public bool Owns(string actionId) => actionId is Edit or Discard;

    public async Task<string> RunAsync(ReaderSnapshot snapshot, string actionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (active.AccountKey is not { } account || snapshot.Body is not { } body)
        {
            return "No account.";
        }

        if (string.Equals(actionId, Edit, StringComparison.Ordinal))
        {
            await composer.OpenAsync(new ComposeRequest(account, ComposeKind.EditDraft) { SourceEmailId = body.Id }, ct).ConfigureAwait(true);
            return "Draft opened.";
        }

        var res = await drafts.DiscardAsync(account, body.Id, ct).ConfigureAwait(true);
        var line = res.IsOk ? "Draft discarded." : "Could not discard the draft: " + res.Error!.Message;
        _ = toasts?.Show(new ToastRequest(line, !res.IsOk));
        return line;
    }
}

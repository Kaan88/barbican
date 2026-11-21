// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:16 MAX_PLUGIN_FETCH_BYTES (25 MB: a body the host hands a plugin in one answer) and host-api-mail.ts:209-248 (the bytes come through the signed-in login, and
// only for a blob of a message the add-on names). Stalwart crates/jmap/src/blob/download.rs (the download URL of the account that owns the blob).
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Plugins.Api.MailActions;

internal static class PluginMailBlobs
{
    /// <summary>The most bytes of one attachment or message source an add-on is given.</summary>
    internal const long MaxFetchBytes = 25L * 1024 * 1024;

    /// <summary>The blob read through the captured login, stopping at <paramref name="max"/> bytes: a larger blob is a failure, never a cut copy.</summary>
    internal static async Task<Result<byte[]>> ReadAsync(IAccountSession session, string accountId, string blobId, string name, string type, long max, CancellationToken ct)
    {
        var opened = await session.Client.DownloadAsync(accountId, blobId, name, type, ct).ConfigureAwait(false);
        if (!opened.IsOk) return Result<byte[]>.Fail(opened.Error!);
        var stream = opened.Value!;
        await using (stream.ConfigureAwait(false))
        {
            using var copy = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, max - total + 1)), ct).ConfigureAwait(false);
                if (read == 0) return Result<byte[]>.Ok(copy.ToArray());
                total += read;
                if (total > max) return Result<byte[]>.Fail(new ValidationError("That is too large to hand to an add-on."));
                await copy.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
    }
}

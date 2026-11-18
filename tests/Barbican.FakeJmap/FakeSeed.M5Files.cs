// The M5 files seed (M5 plan section 4.13, step 1 of files-data): a tree with a trash batch, a shared account's tree, and optionally 3 000 nodes for the chunked get. Synthetic names only.
// A test builds the server with FakeJmapOptions { FileNodeCapability = true, Files = new FakeFiles { SeedM5 = true, SharedAccounts = ["s1"], BulkNodes = 3000, Draft18 = false } }. Owner files-data.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public static partial class FakeSeed
{
    /// <summary>The "Documents" folder of the M5 files tree.</summary>
    public const string FilesDocumentsId = "D1";

    /// <summary>The "Invoices" folder inside Documents.</summary>
    public const string FilesInvoicesId = "D2";

    /// <summary>The ".Trash" folder.</summary>
    public const string FilesTrashId = "D4";

    /// <summary>The dated batch folder of the trash ("1730112000000~D1": deleted from Documents).</summary>
    public const string FilesBatchId = "T1";

    /// <summary>The file inside the trash batch.</summary>
    public const string FilesTrashedFileId = "T2";

    /// <summary>The delete time of the batch, in ms.</summary>
    public const long FilesBatchMs = 1_730_112_000_000;

    /// <summary>The text of "Documents/notes.txt".</summary>
    public const string FilesNotesText = "Meeting notes for the team.\n";

    /// <summary>Fills a files store with the M5 tree: Documents (Invoices with march.txt, notes.txt), Photos (cat.png), a .Trash holding one dated batch and a loose file, a readme at the root, the shared accounts' trees and the bulk folder.</summary>
    public static void FilesM5(FakeFiles files, FakeStore store, string accountId)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(store);
        string Blob(string text) => store.PutBlob(Encoding.UTF8.GetBytes(text));
        _ = files.Add(accountId, null, "Documents", true, id: FilesDocumentsId);
        _ = files.Add(accountId, FilesDocumentsId, "Invoices", true, id: FilesInvoicesId);
        _ = files.Add(accountId, FilesInvoicesId, "march.txt", false, Blob("Invoice March\n"), "text/plain", 14, id: "I1");
        _ = files.Add(accountId, FilesDocumentsId, "notes.txt", false, Blob(FilesNotesText), "text/plain", FilesNotesText.Length, id: "N1");
        _ = files.Add(accountId, null, "Photos", true, id: "D3");
        _ = files.Add(accountId, "D3", "cat.png", false, store.PutBlob([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]), "image/png", 8, id: "P1");
        _ = files.Add(accountId, null, "readme.md", false, Blob("# Readme\n"), "text/markdown", 9, id: "R1");
        _ = files.Add(accountId, null, ".Trash", true, id: FilesTrashId);
        _ = files.Add(accountId, FilesTrashId, "1730112000000~" + FilesDocumentsId, true, id: FilesBatchId);
        _ = files.Add(accountId, FilesBatchId, "old.txt", false, Blob("old\n"), "text/plain", 4, id: FilesTrashedFileId);
        _ = files.Add(accountId, FilesTrashId, "stray.txt", false, Blob("stray\n"), "text/plain", 6, id: "T3");
        foreach (var shared in files.SharedAccounts)
        {
            var rights = new JsonObject { ["mayRead"] = true, ["mayAddChildren"] = true, ["mayRename"] = true, ["mayDelete"] = true, ["mayModifyContent"] = true, ["mayShare"] = false };
            _ = files.Add(shared, null, "Team", true, id: "S1", myRights: (JsonObject)rights.DeepClone());
            _ = files.Add(shared, "S1", "plan.md", false, Blob("# Plan\n"), "text/markdown", 7, id: "S2", myRights: (JsonObject)rights.DeepClone());
            _ = files.Add(shared, null, "Shared notes.txt", false, Blob("shared\n"), "text/plain", 7, id: "S3", myRights: (JsonObject)rights.DeepClone());
        }

        if (files.BulkNodes > 0)
        {
            _ = files.Add(accountId, null, "Bulk", true, id: "B0");
            var blob = Blob("x");
            for (var i = 1; i <= files.BulkNodes; i++)
            {
                _ = files.Add(accountId, "B0", string.Create(CultureInfo.InvariantCulture, $"file-{i:D5}.txt"), false, blob, "text/plain", 1, id: string.Create(CultureInfo.InvariantCulture, $"B{i}"));
            }
        }
    }
}

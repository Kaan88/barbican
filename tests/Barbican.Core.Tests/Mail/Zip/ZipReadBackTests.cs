// M2 plan §6.3 exit test "the zip reads back": an independent reader (System.IO.Compression) opens what ZipArchiveBuilder wrote, with the
// unique names of the three-attachment seed message (two scan.pdf) and a UTF-8 name.

using System.IO.Compression;
using System.Text;
using Barbican.Core.Mail.Zip;

namespace Barbican.Core.Tests.Mail.Zip;

public sealed class ZipReadBackTests
{
    private static readonly string[] Texts = ["%PDF first", "%PDF second", "notes", "hallo", "x"];

    [Fact]
    public void System_io_compression_reads_the_archive()
    {
        var names = ZipArchiveBuilder.UniqueNames(["scan.pdf", "scan.pdf", "notes.txt", "Grüße.txt", string.Empty], "attachment");
        var data = Texts.Select(Encoding.UTF8.GetBytes).ToArray();
        var built = ZipArchiveBuilder.Build([.. names.Select((n, i) => new ZipEntryData(n, data[i]))], new DateTime(2026, 11, 17, 9, 30, 0));
        Assert.True(built.IsOk);

        using var zip = new ZipArchive(new MemoryStream(built.Value!), ZipArchiveMode.Read);
        Assert.Equal(["scan.pdf", "scan (1).pdf", "notes.txt", "Grüße.txt", "attachment"], zip.Entries.Select(e => e.FullName).ToArray());
        for (var i = 0; i < names.Count; i++)
        {
            using var s = zip.Entries[i].Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            Assert.Equal(data[i], m.ToArray());
            Assert.Equal(new DateTime(2026, 11, 17, 9, 30, 0), zip.Entries[i].LastWriteTime.DateTime);
        }
    }

    [Fact]
    public void Too_many_files_are_refused()
    {
        var many = Enumerable.Range(0, 0x10000).Select(i => new ZipEntryData("f", [])).ToList();
        Assert.False(ZipArchiveBuilder.Build(many, DateTime.UnixEpoch).IsOk);
    }
}

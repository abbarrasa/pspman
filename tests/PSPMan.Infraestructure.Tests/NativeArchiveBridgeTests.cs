using System.Security.Cryptography;

namespace PSPMan.Infraestructure.Tests;

/// <summary>T01 API gate, deliberately independent of the future production adapter.</summary>
public sealed class NativeArchiveBridgeTests
{
    [NativeBridgeTheory]
    [InlineData("Zip.deflate.zip", null)]
    [InlineData("Zip.zip64.zip", null)]
    [InlineData("Zip.deflate.WinzipAES.zip", "test")]
    [InlineData("Zip.lzma.WinzipAES.zip", "test")]
    [InlineData("Zip.deflate.pkware.zip", "12345678")]
    [InlineData("WinZip26.nocomp.multi.zip", null)]
    [InlineData("Rar4.rar", null)]
    [InlineData("Rar5.rar", null)]
    [InlineData("Rar5.solid.rar", null)]
    [InlineData("Rar.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar2.multi.rar", null)]
    [InlineData("Rar5.multi.part01.rar", null)]
    [InlineData("Rar.EncryptedParts.part01.rar", "test")]
    [InlineData("Rar5.multi.part03.rar", null)]
    [InlineData("Rar2.multi.r02", null)]
    [InlineData("WinZip26.nocomp.multi.z01", null)]
    public void Native_callbacks_extract_expected_payloads(string name, string? password)
    {
        using var probe = new NativeArchiveProbe(name, password);
        Assert.Equal(0, probe.Open());
        var files = probe.Metadata().Where(e => (e.Entry.Flags & 1) == 0).ToArray();
        Assert.All(files, e => Assert.NotEqual(0u, e.Entry.Flags & 8));
        Assert.Equal(name == "Zip.deflate.pkware.zip" ? 19UL : 100926UL,
            files.Aggregate(0UL, (total, e) => total + e.Entry.Size));
        Assert.Equal(0, probe.Extract());
        if (name == "Zip.deflate.pkware.zip")
        {
            Assert.Equal("bla-bla-bla-bla-bla", System.Text.Encoding.ASCII.GetString(Assert.Single(probe.Outputs.Values)));
            Assert.Equal(19UL, probe.Decoded);
            Assert.Equal(probe.Decoded, probe.Written);
            return;
        }
        var hashes = probe.Outputs.Values.Select(bytes => Convert.ToHexStringLower(SHA256.HashData(bytes))).Order().ToArray();
        Assert.Equal(new[]
        {
            "4d581d93d369f6e1c9b295ff38d82dabd577f927dfaf0c35818c015c85e322d9",
            "8557928804f57ecc340b3bb38b095a3607474ec8deb0076f316fcfe02b562106",
            "b251c7501fb0f55dd4a92feabe0a6f5733bc40a02679498155fae9b30138fc53"
        }, hashes);
        Assert.Equal(100926UL, probe.Decoded);
        Assert.Equal(probe.Decoded, probe.Written);
    }
}


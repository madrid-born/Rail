using System.Security.Cryptography;
using System.Text;
using Rail.Maui.Services;
using Xunit;

namespace Rail.Tests.Maui;

public class PackagedMapArchiveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"rail-offline-map-{Guid.NewGuid():N}");
    private readonly byte[] bytes = Enumerable.Range(0, 100_000).Select(i => (byte)i).ToArray();
    private int opens;

    private PackagedMapArchive Create(string? version = null) => new(
        () => { opens++; return Task.FromResult<Stream>(new UnseekableAsset(bytes)); },
        () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(
            version ?? Convert.ToHexString(SHA256.HashData(bytes))))), directory);

    [Fact]
    public async Task ReadsExactRangesFromNonSeekablePackageAndReusesVerifiedCopyAfterRestart()
    {
        var archive = Create();
        var reads = await Task.WhenAll(archive.ReadBase64Async(0, 127), archive.ReadBase64Async(1024, 4096));
        Assert.Equal(bytes[..127], Convert.FromBase64String(reads[0]));
        Assert.Equal(bytes[1024..5120], Convert.FromBase64String(reads[1]));
        Assert.Equal(1, opens);
        Assert.Equal(bytes[^100..], Convert.FromBase64String(await Create().ReadBase64Async(bytes.Length - 100, 100)));
        Assert.Equal(1, opens);
    }

    [Fact]
    public async Task RejectsInvalidRangesAndNeverReturnsPartialOrUnboundedData()
    {
        var archive = Create();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => archive.ReadBase64Async(-1, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => archive.ReadBase64Async(0, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => archive.ReadBase64Async(0, 256 * 1024 + 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => archive.ReadBase64Async(bytes.Length - 1, 2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => archive.ReadBase64Async(long.MaxValue, 1));
    }

    [Fact]
    public async Task FailedVerificationLeavesOldArchiveIntactAndRemovesIncompleteCopy()
    {
        Directory.CreateDirectory(directory);
        var old = Path.Combine(directory, new string('a', 64) + ".pmtiles");
        await File.WriteAllTextAsync(old, "previous map");
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(new string('b', 64)).ReadBase64Async(0, 1));
        Assert.Equal([old], Directory.GetFiles(directory));
        await Create().ReadBase64Async(0, 1);
        Assert.False(File.Exists(old));
        Assert.Single(Directory.GetFiles(directory, "*.pmtiles"));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }

    private sealed class UnseekableAsset(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
}

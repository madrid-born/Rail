using System.Security.Cryptography;

namespace Rail.Maui.Services;

// Pure file I/O, shared by Windows/Android. Android package streams need not be
// seekable, so prepare one private, verified copy and then read small ranges.
public sealed class PackagedMapArchive(
    Func<Task<Stream>> openPackage,
    Func<Task<Stream>> openVersion,
    string directory)
{
    public const int MaximumReadLength = 256 * 1024;
    private readonly SemaphoreSlim preparation = new(1, 1);
    private string? preparedPath;

    public async Task<string> ReadBase64Async(long offset, int length)
    {
        if (offset < 0 || length <= 0 || length > MaximumReadLength)
            throw new ArgumentOutOfRangeException(nameof(length), "Invalid map byte range.");
        var path = await PrepareAsync().ConfigureAwait(false);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
        if (offset > file.Length || length > file.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset), "Map byte range is outside the archive.");
        file.Seek(offset, SeekOrigin.Begin);
        var bytes = new byte[length];
        await file.ReadExactlyAsync(bytes).ConfigureAwait(false);
        return Convert.ToBase64String(bytes);
    }

    private async Task<string> PrepareAsync()
    {
        await preparation.WaitAsync().ConfigureAwait(false);
        try
        {
            if (preparedPath is not null && File.Exists(preparedPath)) return preparedPath;
            await using var versionStream = await openVersion().ConfigureAwait(false);
            using var reader = new StreamReader(versionStream);
            var hash = (await reader.ReadToEndAsync().ConfigureAwait(false)).Trim().ToLowerInvariant();
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid packaged map version.");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, $"{hash}.pmtiles");
            if (!File.Exists(target))
            {
                var temporary = Path.Combine(directory, $"{hash}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await using (var source = await openPackage().ConfigureAwait(false))
                    await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 81920, FileOptions.Asynchronous))
                        await source.CopyToAsync(destination).ConfigureAwait(false);
                    await using (var copy = File.OpenRead(temporary))
                    {
                        var actual = Convert.ToHexString(await SHA256.HashDataAsync(copy).ConfigureAwait(false));
                        if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("The packaged map failed verification.");
                    }
                    File.Move(temporary, target, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            preparedPath = target;
            // Only our own old, content-addressed map copies are removed after
            // the new archive has been verified and installed successfully.
            foreach (var old in Directory.EnumerateFiles(directory, "*.pmtiles"))
            {
                var name = Path.GetFileNameWithoutExtension(old);
                if (name.Length != 64 || !name.All(Uri.IsHexDigit) || old == target) continue;
                try { File.Delete(old); } catch (IOException) { /* An older view may still be reading. */ }
            }
            return target;
        }
        finally { preparation.Release(); }
    }
}

using System.Collections.Concurrent;
using System.Text.Json;

namespace Archipaper.Services;

public sealed class JsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task<T> LoadAsync<T>(string path, Func<T> fallback)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                try { return await ReadAsync<T>(path).ConfigureAwait(false); }
                catch (JsonException ex)
                {
                    AppLog.Error(ex);
                    File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                    if (!File.Exists(path + ".bak")) throw;
                }
            }
            return File.Exists(path + ".bak") ? await ReadAsync<T>(path + ".bak").ConfigureAwait(false) : fallback();
        }
        finally { gate.Release(); }
    }

    private static async Task<T> ReadAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options).ConfigureAwait(false)
            ?? throw new JsonException("Empty JSON value: " + Path.GetFileName(path));
    }

    public Task SaveAsync<T>(string path, T value) => SaveBytesAsync(Path.GetFullPath(path), JsonSerializer.SerializeToUtf8Bytes(value, Options));

    private static async Task SaveBytesAsync(string path, byte[] snapshot)
    {
        var gate = Gates.GetOrAdd(path, _ => new(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temp, snapshot).ConfigureAwait(false);
            if (File.Exists(path))
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
                    File.Copy(path, path + ".bak", true);
                }
                catch (JsonException) { /* Preserve the last valid backup. */ }
            }
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            finally { gate.Release(); }
        }
    }
}

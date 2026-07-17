using System.Text.Json;

namespace Archipaper.Services;

public sealed class JsonStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<T> LoadAsync<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options) ?? fallback();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return fallback();
        }
    }

    public async Task SaveAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, value, Options);
        File.Move(temp, path, true);
    }
}

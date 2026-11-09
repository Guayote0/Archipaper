using System.Windows.Media.Imaging;
namespace Archipaper.Services;

public static class ImageValidation
{
    public const long MaxBytes = 64L * 1024 * 1024;
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp" };
    public static ImageInfo Inspect(string path, bool fullSize = false, bool decode = false)
    {
        var length = new FileInfo(path).Length;
        if (length <= 0 || length > MaxBytes) throw new InvalidDataException("Empty image or image larger than 64 MiB.");
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        var count = stream.Read(header);
        var extension = count >= 3 && header[0] == 255 && header[1] == 216 && header[2] == 255 ? ".jpg" :
            count == 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ? ".png" :
            count >= 2 && header[0] == 66 && header[1] == 77 ? ".bmp" : throw new InvalidDataException("Unsupported image bytes.");
        stream.Position = 0;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var width = frame.PixelWidth; var height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > 80_000_000) throw new InvalidDataException("Invalid image dimensions or image larger than 80 megapixels.");
        if (fullSize && (Math.Min(width, height) < 1200 || Math.Max(width, height) < 1800)) throw new InvalidDataException("Wallpaper requires at least 1200 × 1800 pixels, either orientation.");
        if (decode)
        {
            stream.Position = 0;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = Math.Min(width, 1200); bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        }
        return new(width, height, extension);
    }
}
public sealed record ImageInfo(int Width, int Height, string Extension);

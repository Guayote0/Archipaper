using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using Archipaper.Models;

namespace Archipaper.Services;

public sealed class CaptionedWallpaperService
{
    public string Create(string sourcePath, ApprovedImageMetadata metadata, MonitorInfo monitor)
    {
        var architect = metadata.Architect?.Trim() ?? "";
        var project = string.IsNullOrWhiteSpace(metadata.ProjectName)
            ? ArchitectureMetadata.CleanProjectName(metadata.Title, architect)
            : metadata.ProjectName.Trim();
        var identity = $"{sourcePath}|{File.GetLastWriteTimeUtc(sourcePath).Ticks}|{monitor.Width}|" +
            $"{monitor.Height}|{architect}|{project}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..24];
        var output = Path.Combine(AppPaths.CaptionCache, hash + ".jpg");
        if (File.Exists(output)) return output;

        using var source = new System.Drawing.Bitmap(sourcePath);
        using var canvas = new System.Drawing.Bitmap(monitor.Width, monitor.Height, PixelFormat.Format24bppRgb);
        using var graphics = System.Drawing.Graphics.FromImage(canvas);
        graphics.Clear(System.Drawing.Color.Black);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var scale = Math.Max((double)monitor.Width / source.Width, (double)monitor.Height / source.Height);
        var drawWidth = (int)Math.Ceiling(source.Width * scale);
        var drawHeight = (int)Math.Ceiling(source.Height * scale);
        graphics.DrawImage(source, (monitor.Width - drawWidth) / 2, (monitor.Height - drawHeight) / 2,
            drawWidth, drawHeight);

        var lines = string.IsNullOrWhiteSpace(architect) ? 1 : 2;
        var padding = Math.Max(14, monitor.Width / 80);
        var captionWidth = Math.Min(monitor.Width - padding * 2, Math.Max(340, (int)(monitor.Width * .42)));
        var lineHeight = Math.Max(19, monitor.Height / 55);
        var captionHeight = lineHeight * lines + padding;
        var left = padding;
        var top = monitor.Height - captionHeight - padding;
        using var panelBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(142, 10, 10, 10));
        graphics.FillRectangle(panelBrush, left - padding / 2, top - padding / 2,
            captionWidth + padding, captionHeight + padding / 2);

        var fontSize = Math.Max(11f, monitor.Height / 88f);
        using var font = new System.Drawing.Font("Segoe UI", fontSize, System.Drawing.FontStyle.Regular,
            System.Drawing.GraphicsUnit.Pixel);
        using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        using var format = new System.Drawing.StringFormat
        {
            Trimming = System.Drawing.StringTrimming.EllipsisCharacter,
            FormatFlags = System.Drawing.StringFormatFlags.NoWrap
        };
        if (string.IsNullOrWhiteSpace(architect))
        {
            graphics.DrawString($"PROJECT: {project}", font, textBrush,
                new System.Drawing.RectangleF(left, top, captionWidth, lineHeight), format);
        }
        else
        {
            graphics.DrawString($"ARCHITECT: {architect}", font, textBrush,
                new System.Drawing.RectangleF(left, top, captionWidth, lineHeight), format);
            graphics.DrawString($"PROJECT: {project}", font, textBrush,
                new System.Drawing.RectangleF(left, top + lineHeight, captionWidth, lineHeight), format);
        }

        canvas.Save(output, ImageFormat.Jpeg);
        return output;
    }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VibeTerm.Services;

/// <summary>Reads the Windows clipboard on the UI thread, then saves attachments off-thread.</summary>
public static class ClipboardPasteService
{
    public static async Task<string?> ReadAsync(string? workingDirectory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var data = Clipboard.GetDataObject();
                return data == null ? null : await PrepareAsync(data, workingDirectory);
            }
            catch (ExternalException) when (attempt < 3)
            {
                // Another application may briefly own the clipboard.
                await Task.Delay(50);
            }
        }
    }

    public static Task<string?> PrepareAsync(IDataObject data, string? workingDirectory)
    {
        // Explorer offers several formats at once. Actual files take priority over previews/text.
        if (data.GetDataPresent(DataFormats.FileDrop) &&
            data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            var sources = files.ToArray();
            string directory = RequireDirectory(workingDirectory);
            return Task.Run<string?>(() => SaveFiles(sources, directory));
        }

        BitmapSource? image = null;
        if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream png)
        {
            // OnLoad detaches the bitmap from the clipboard-owned stream.
            using var copy = new MemoryStream();
            if (png.CanSeek) png.Position = 0;
            png.CopyTo(copy);
            copy.Position = 0;
            image = BitmapFrame.Create(copy, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        }
        else if (data.GetDataPresent(DataFormats.Bitmap))
        {
            image = data.GetData(DataFormats.Bitmap) as BitmapSource;
        }

        if (image != null)
        {
            string directory = RequireDirectory(workingDirectory);
            // Copy pixels, not decoder metadata: even a frozen BitmapFrame can retain
            // a dispatcher-bound decoder. JPEG needs an opaque RGB image anyway.
            var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            int stride = checked(rgb.PixelWidth * 3);
            var pixels = new byte[checked(stride * rgb.PixelHeight)];
            rgb.CopyPixels(pixels, stride, 0);
            var snapshot = BitmapSource.Create(rgb.PixelWidth, rgb.PixelHeight,
                rgb.DpiX, rgb.DpiY, PixelFormats.Bgr24, null, pixels, stride);
            snapshot.Freeze();
            return Task.Run<string?>(() => SaveImage(snapshot, directory));
        }

        string? text = data.GetData(DataFormats.UnicodeText) as string
            ?? data.GetData(DataFormats.Text) as string;
        return Task.FromResult(text);
    }

    private static string RequireDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            throw new IOException("Текущий каталог терминала ещё не определён.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Каталог терминала недоступен: {directory}");
        return Path.GetFullPath(directory);
    }

    private static string SaveFiles(string[] sources, string directory)
    {
        // Validate the complete selection before creating any files.
        foreach (string source in sources)
        {
            if (Directory.Exists(source))
                throw new IOException("Вставка папок не поддерживается. Скопируйте отдельные файлы.");
            if (!Path.IsPathFullyQualified(source) || !File.Exists(source))
                throw new FileNotFoundException("Файл из буфера обмена недоступен.", source);
        }

        var saved = new List<string>();
        try
        {
            foreach (string source in sources)
            {
                using var input = File.OpenRead(source);
                using var output = CreateUniqueFile(directory, Path.GetFileName(source));
                saved.Add(output.Name);
                input.CopyTo(output);
            }
            return string.Join(" ", saved.Select(QuotePath));
        }
        catch
        {
            foreach (string path in saved) TryDelete(path);
            throw;
        }
    }

    private static string SaveImage(BitmapSource image, string directory)
    {
        var output = CreateUniqueFile(directory, $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.jpg");
        string path = output.Name;
        try
        {
            using (output)
            {
                var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(output);
            }
            return QuotePath(path);
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static FileStream CreateUniqueFile(string directory, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int suffix = 0; ; suffix++)
        {
            string candidate = Path.Combine(directory, suffix == 0 ? name : $"{stem} ({suffix}){extension}");
            try
            {
                // CreateNew makes simultaneous pastes in different panels safe too.
                return new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
        }
    }

    private static string QuotePath(string path)
    {
        var quoted = new StringBuilder(path.Length + 2).Append('\'');
        foreach (char ch in path)
        {
            // PowerShell also treats these typographic marks as single quotes.
            if (ch is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B') quoted.Append(ch);
            quoted.Append(ch);
        }
        return quoted.Append('\'').ToString();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

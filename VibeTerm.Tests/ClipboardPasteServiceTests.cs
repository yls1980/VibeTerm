using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VibeTerm.Services;
using Xunit;

namespace VibeTerm.Tests;

// Blocking waits run only on our dedicated STA thread, never xUnit's synchronization context.
#pragma warning disable xUnit1031
public sealed class ClipboardPasteServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VibeTerm.Tests", Guid.NewGuid().ToString("N"));

    public ClipboardPasteServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void PlainTextDoesNotRequireDirectoryAndKeepsUnicodeAndNewlines() => OnSta(() =>
    {
        const string text = "Привет\r\nworld 🖼";
        var data = new DataObject(DataFormats.UnicodeText, text);
        Assert.Equal(text, ClipboardPasteService.PrepareAsync(data, null).GetAwaiter().GetResult());
    });

    [Fact]
    public void FilesTakePriorityOverTextAndCopyBytesWithoutOverwriting() => OnSta(() =>
    {
        string source = Path.Combine(_root, "отчёт 1.txt");
        File.WriteAllText(source, "original");
        string target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "отчёт 1.txt"), "existing");
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { source });
        data.SetText("not a file path");

        string? result = ClipboardPasteService.PrepareAsync(data, target).GetAwaiter().GetResult();

        Assert.Equal($"'{Path.Combine(target, "отчёт 1 (1).txt")}'", result);
        Assert.Equal("original", File.ReadAllText(Path.Combine(target, "отчёт 1 (1).txt")));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(target, "отчёт 1.txt")));
        Assert.Equal("original", File.ReadAllText(source));
    });

    [Fact]
    public void FileAlreadyInCurrentDirectoryGetsSeparateCopy() => OnSta(() =>
    {
        string source = Path.Combine(_root, "notes.txt");
        File.WriteAllText(source, "keep");
        var data = new DataObject(DataFormats.FileDrop, new[] { source });
        string? result = ClipboardPasteService.PrepareAsync(data, _root).GetAwaiter().GetResult();
        Assert.Equal($"'{Path.Combine(_root, "notes (1).txt")}'", result);
        Assert.Equal("keep", File.ReadAllText(source));
    });

    [Fact]
    public void MultipleFilesPreserveOrderAndEscapeQuotes() => OnSta(() =>
    {
        string first = Path.Combine(_root, "it's a file.txt");
        string second = Path.Combine(_root, "second’s.dat");
        File.WriteAllText(first, "one");
        File.WriteAllBytes(second, new byte[] { 0, 1, 255 });
        string target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var data = new DataObject(DataFormats.FileDrop, new[] { first, second });
        string? result = ClipboardPasteService.PrepareAsync(data, target).GetAwaiter().GetResult();
        Assert.Equal($"'{Path.Combine(target, "it''s a file.txt")}' '{Path.Combine(target, "second’’s.dat")}'", result);
        Assert.Equal(File.ReadAllBytes(second), File.ReadAllBytes(Path.Combine(target, Path.GetFileName(second))));
        Assert.DoesNotContain('\n', result!);
    });

    [Fact]
    public void LaterReadFailureRollsBackCopiesButPreservesExistingFiles() => OnSta(() =>
    {
        string first = Path.Combine(_root, "one.txt");
        string second = Path.Combine(_root, "two.txt");
        File.WriteAllText(first, "one");
        File.WriteAllText(second, "two");
        string target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        string existing = Path.Combine(target, "one.txt");
        File.WriteAllText(existing, "keep");
        using var locked = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var data = new DataObject(DataFormats.FileDrop, new[] { first, second });
        Assert.Throws<IOException>(() => ClipboardPasteService.PrepareAsync(data, target).GetAwaiter().GetResult());
        Assert.Equal(new[] { existing }, Directory.GetFiles(target));
        Assert.Equal("keep", File.ReadAllText(existing));
    });

    [Fact]
    public void MissingTargetDoesNotFallBackToApplicationDirectory() => OnSta(() =>
    {
        string source = Path.Combine(_root, "one.txt");
        File.WriteAllText(source, "one");
        var data = new DataObject(DataFormats.FileDrop, new[] { source });
        Assert.Throws<DirectoryNotFoundException>(() => { _ = ClipboardPasteService.PrepareAsync(data, Path.Combine(_root, "missing")); });
        Assert.Throws<IOException>(() => { _ = ClipboardPasteService.PrepareAsync(data, null); });
        Assert.Single(Directory.GetFiles(_root));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScreenshotIsSavedAsDecodableJpegInRequestedDirectory(bool pngFormat) => OnSta(() =>
    {
        var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgr24, null,
            new byte[] { 0, 0, 255, 0, 255, 0 }, 6);
        var data = new DataObject();
        using var png = new MemoryStream();
        if (pngFormat)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(png);
            png.Position = 0;
            data.SetData("PNG", png);
        }
        else data.SetData(DataFormats.Bitmap, bitmap);

        string? result = ClipboardPasteService.PrepareAsync(data, _root).GetAwaiter().GetResult();
        string path = Assert.Single(Directory.GetFiles(_root));
        Assert.Equal($"'{path}'", result);
        Assert.Equal(".jpg", Path.GetExtension(path));
        using var stream = File.OpenRead(path);
        var decoded = new JpegBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(2, decoded.Frames[0].PixelWidth);
        Assert.Equal(1, decoded.Frames[0].PixelHeight);
    });

    [Fact]
    public void ConcurrentPastesNeverOverwriteEachOther() => OnSta(() =>
    {
        string source = Path.Combine(_root, "one.bin");
        File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
        string target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var data = new DataObject(DataFormats.FileDrop, new[] { source });
        var tasks = Enumerable.Range(0, 8).Select(_ => ClipboardPasteService.PrepareAsync(data, target)).ToArray();
        var results = Task.WhenAll(tasks).GetAwaiter().GetResult();
        Assert.Equal(8, results.Distinct().Count());
        Assert.All(Directory.GetFiles(target), path => Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path)));
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

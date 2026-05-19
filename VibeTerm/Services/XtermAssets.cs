using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace VibeTerm.Services;

/// <summary>
/// Downloads xterm.js assets to the Resources folder on first run so the
/// terminal works offline on subsequent launches.
/// Falls back to CDN automatically in terminal.html if files are missing.
/// </summary>
internal static class XtermAssets
{
    private const string XtermVersion = "5.3.0";
    private const string FitVersion   = "0.8.0";
    private const string CdnBase      = "https://cdn.jsdelivr.net/npm";

    // (CDN URL, local file name)
    private static readonly (string Url, string File)[] Assets = new[]
    {
        ($"{CdnBase}/xterm@{XtermVersion}/lib/xterm.js",            "xterm.js"),
        ($"{CdnBase}/xterm@{XtermVersion}/css/xterm.css",           "xterm.css"),
        ($"{CdnBase}/xterm-addon-fit@{FitVersion}/lib/xterm-addon-fit.js", "xterm-addon-fit.js"),
    };

    /// <summary>
    /// Directory that is served via the "app.local" virtual host in WebView2.
    /// Contains terminal.html (copied from project) and xterm.js assets (downloaded).
    /// </summary>
    internal static string ResourcesDir =>
        Path.Combine(AppContext.BaseDirectory, "Resources");

    internal static bool AllPresent()
    {
        foreach (var (_, file) in Assets)
            if (!File.Exists(Path.Combine(ResourcesDir, file)))
                return false;
        return true;
    }

    /// <summary>
    /// Downloads any missing xterm.js files in the background.
    /// Silently skips files that already exist or can't be downloaded.
    /// </summary>
    internal static Task EnsureAssetsAsync() => Task.Run(async () =>
    {
        if (AllPresent()) return;

        string dir = ResourcesDir;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent
            .ParseAdd("VibeTerm/1.0 (xterm-asset-downloader)");

        foreach (var (url, fileName) in Assets)
        {
            string dest = Path.Combine(dir, fileName);
            if (File.Exists(dest)) continue;

            try
            {
                byte[] data = await http.GetByteArrayAsync(url).ConfigureAwait(false);
                // Write to a temp file first, then rename — avoids partial writes
                string tmp = dest + ".tmp";
                await File.WriteAllBytesAsync(tmp, data).ConfigureAwait(false);
                File.Move(tmp, dest, overwrite: true);
            }
            catch
            {
                // Non-fatal: terminal.html will fall back to CDN for this file
                try { File.Delete(dest + ".tmp"); } catch { }
            }
        }
    });
}

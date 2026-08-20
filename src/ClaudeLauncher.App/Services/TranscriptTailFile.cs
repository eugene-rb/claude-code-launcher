using System.IO;
using System.Text;

namespace ClaudeLauncher.App.Services;

/// <summary>Shared tail-read used by both <see cref="TranscriptActivityClassifier"/> and
/// <see cref="TranscriptPreviewReader"/>, which independently need only the last turn or two of a
/// transcript. Factored out so a caller needing both (the dashboard's per-tick refresh) can read the
/// file once and hand the same text to both, instead of two separate disk reads per tick.</summary>
internal static class TranscriptTailFile
{
    private const int DefaultTailBytes = 64 * 1024;

    public static string? ReadTail(string path, int maxBytes = DefaultTailBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var start = Math.Max(0, fs.Length - maxBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[fs.Length - start];
            var read = fs.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

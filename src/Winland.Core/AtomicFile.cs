using System.Text;

namespace Winland.Core;

/// <summary>Writes a file so that a reader, or a process stopped half way, never finds part of it.</summary>
public static class AtomicFile
{
    // UTF-8 with no byte order mark, as File.WriteAllText writes it.
    private static readonly UTF8Encoding Written = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Writes <paramref name="text"/> to a file beside <paramref name="path"/>, waits until it is on the disk, and renames it
    /// over <paramref name="path"/>, so a power cut leaves the old file or the new one.
    /// When the rename is refused, as while another program holds the file open, the text is written in place.
    /// When the file beside it cannot be written, as on a full disk, this throws and <paramref name="path"/> is left as it was.
    /// </summary>
    /// <param name="path">The file to replace. Its folder must exist.</param>
    /// <param name="text">The whole new content.</param>
    public static void Write(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(path, Written.GetBytes(text));
    }

    /// <summary>Writes <paramref name="bytes"/> as <see cref="Write(string, string)"/> writes text, for a file whose encoding is not Winoma's to choose.</summary>
    /// <param name="path">The file to replace. Its folder must exist.</param>
    /// <param name="bytes">The whole new content.</param>
    public static void Write(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);
        var staged = path + ".tmp";

        // Not under the fallback below. Writing in place empties the file first, and a disk too full for the staged file
        // is too full for that write as well, so the file that was whole would be lost.
        using (var stream = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes);

            // Windows may record the rename before the bytes. A power cut between the two leaves the file at its new length
            // and full of zeros, so the bytes are on the disk before the rename is asked for.
            stream.Flush(flushToDisk: true);
        }

        try
        {
            File.Move(staged, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.WriteAllBytes(path, bytes);
            try
            {
                File.Delete(staged);
            }
            catch (Exception leftover) when (leftover is IOException or UnauthorizedAccessException)
            {
                // The next save writes over it.
            }
        }
    }
}

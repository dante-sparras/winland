namespace Winland.Core;

/// <summary>The word in <c>lifetime</c>: what the desktop that wrote it was doing, or what whoever ends it means to do.</summary>
public enum LifetimeWord
{
    /// <summary>A desktop is up. One that exits under this word crashed.</summary>
    Running,

    /// <summary>The desktop was asked to Stop, or the session is ending. Winoma stays down (ADR 0067).</summary>
    Stopped,

    /// <summary>The desktop is ended so that another build takes its place: a deploy, or an Update (ADR 0068). The next one is a moment away.</summary>
    Replacing,
}

/// <summary>What the desktop watcher does about a desktop that has exited.</summary>
public enum ExitStep
{
    /// <summary>Do nothing. A desktop holds the Host, and what the old one left is the new one's now.</summary>
    Leave,

    /// <summary>Do nothing yet, and ask again in a moment: the desktop that takes this one's place may still come.</summary>
    Wait,

    /// <summary>Put back the windows the desktop left hidden, and start it again.</summary>
    ShowAndRestart,

    /// <summary>Put back the windows the desktop left hidden, give Windows its own back, and leave Winoma down.</summary>
    ShowAndStayDown,
}

/// <summary>
/// The <c>lifetime</c> file in the person's folder, and what its word asks of the two programs that read it:
/// the desktop watcher, once its desktop has exited, and a desktop that starts (ADR 0059).
/// </summary>
public static class Lifetime
{
    /// <summary>The file's name in the user folder.</summary>
    public const string FileName = "lifetime";

    /// <summary>
    /// How long the watcher of a desktop that was replaced waits for the next one before it shows what was left hidden.
    /// A deploy whose copy failed, or an Update that never started the new build, must not leave windows hidden for good.
    /// </summary>
    public static readonly TimeSpan ReplaceLimit = TimeSpan.FromSeconds(30);

    /// <summary>The word as the file holds it.</summary>
    /// <param name="word">The word.</param>
    /// <returns><c>running</c>, <c>stopped</c>, or <c>replacing</c>.</returns>
    public static string Text(LifetimeWord word) => word switch
    {
        LifetimeWord.Stopped => "stopped",
        LifetimeWord.Replacing => "replacing",
        _ => "running",
    };

    /// <summary>Reads the file's text.</summary>
    /// <param name="text">The file's text. Null when there is no file.</param>
    /// <returns>The word.</returns>
    public static LifetimeWord Read(string? text) => text?.Trim() switch
    {
        "stopped" => LifetimeWord.Stopped,
        "replacing" => LifetimeWord.Replacing,
        _ => LifetimeWord.Running,
    };

    /// <summary>
    /// How long to wait before the file is asked for again, after another program had it open. The watcher of a replaced desktop reads the file
    /// at the moment the next desktop writes it, so a refusal is expected and short.
    /// </summary>
    /// <param name="failed">How many times in a row the file was refused, from one.</param>
    /// <returns>The wait before the next try. Null when it is time to give up: six tries, a third of a second in all.</returns>
    public static TimeSpan? RetryAfter(int failed) =>
        failed is >= 1 and <= 5 ? TimeSpan.FromMilliseconds(10 << (failed - 1)) : null;

    /// <summary>
    /// Reads the word from the file. The file is opened so that a program writing it at the same moment is not refused,
    /// and a file another program holds is asked for again (<see cref="RetryAfter"/>).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="word">The word. <see cref="LifetimeWord.Running"/> when there is no file, and when the file could not be read.</param>
    /// <param name="error">Why the file could not be read, or null.</param>
    /// <param name="pause">Waits between two tries. Null sleeps.</param>
    /// <returns>False when the file is there and could not be read.</returns>
    public static bool TryRead(string path, out LifetimeWord word, out Exception? error, Action<TimeSpan>? pause = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var read = LifetimeWord.Running;
        var done = Persist(
            () =>
            {
                try
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, Shared);
                    using var reader = new StreamReader(file);
                    read = Read(reader.ReadToEnd());
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    // No file is a desktop that left no word.
                }
            },
            pause,
            out error);
        word = read;
        return done;
    }

    /// <summary>
    /// Writes the word into the file, and makes the folder when it is missing. The file is opened so that a program reading it
    /// at the same moment is not refused, and a file another program holds is asked for again (<see cref="RetryAfter"/>).
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="word">The word.</param>
    /// <param name="error">Why the word was not written, or null.</param>
    /// <param name="pause">Waits between two tries. Null sleeps.</param>
    /// <returns>False when the word was not written. The file then holds what it held, and whoever reads it acts on that word.</returns>
    public static bool TryWrite(string path, LifetimeWord word, out Exception? error, Action<TimeSpan>? pause = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var bytes = System.Text.Encoding.ASCII.GetBytes(Text(word));
        return Persist(
            () =>
            {
                if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
                {
                    Directory.CreateDirectory(folder);
                }

                // Written over the old word and then cut to length, so the file is never empty for a reader that looks in between.
                using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, Shared);
                file.Write(bytes);
                file.SetLength(bytes.Length);
            },
            pause,
            out error);
    }

    // A reader and a writer may both have the file open, and so may whoever replaces or removes it.
    private const FileShare Shared = FileShare.ReadWrite | FileShare.Delete;

    // Windows' ERROR_SHARING_VIOLATION and ERROR_LOCK_VIOLATION: another program has the file open and does not share it.
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    /// <summary>Does <paramref name="work"/>, again for as long as another program holds the file and <see cref="RetryAfter"/> gives a wait.</summary>
    private static bool Persist(Action work, Action<TimeSpan>? pause, out Exception? error)
    {
        for (var failed = 1; ; failed++)
        {
            try
            {
                work();
                error = null;
                return true;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is SharingViolation or LockViolation && RetryAfter(failed) is TimeSpan wait)
            {
                (pause ?? Thread.Sleep)(wait);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex;
                return false;
            }
        }
    }

    /// <summary>What the desktop watcher does once its desktop has exited. It asks again for as long as the answer is to wait.</summary>
    /// <param name="word">The word the file held when the desktop exited.</param>
    /// <param name="arrived">True when a desktop holds the Host now.</param>
    /// <param name="waited">How long ago the desktop exited.</param>
    /// <returns>The step to take.</returns>
    public static ExitStep AfterExit(LifetimeWord word, bool arrived, TimeSpan waited)
    {
        if (arrived)
        {
            return ExitStep.Leave;
        }

        return word switch
        {
            LifetimeWord.Stopped => ExitStep.ShowAndStayDown,
            LifetimeWord.Replacing => waited < ReplaceLimit ? ExitStep.Wait : ExitStep.ShowAndStayDown,
            _ => ExitStep.ShowAndRestart,
        };
    }

    /// <summary>
    /// True when a desktop that starts takes over the windows the last one left hidden, still hidden, and shows only the ones it will not keep so
    /// (the gateway's left-hidden list). False when it shows every one of them at once.
    /// </summary>
    /// <param name="found">The word the file held when the desktop started, before it wrote its own.</param>
    /// <returns>True for a desktop that was replaced. After a crash or a Stop a window still hidden has nothing else to show it.</returns>
    public static bool TakesOverHidden(LifetimeWord found) => found == LifetimeWord.Replacing;
}

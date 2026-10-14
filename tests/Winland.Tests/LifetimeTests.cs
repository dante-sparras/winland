using FluentAssertions;
using Winland.Core;

namespace Winoma.Tests;

[TestClass]
public sealed class LifetimeTests
{
    [TestMethod]
    public void The_file_holds_one_of_three_words()
    {
        Lifetime.Text(LifetimeWord.Running).Should().Be("running");
        Lifetime.Text(LifetimeWord.Stopped).Should().Be("stopped");
        Lifetime.Text(LifetimeWord.Replacing).Should().Be("replacing");

        Lifetime.Read("running").Should().Be(LifetimeWord.Running);
        Lifetime.Read("stopped").Should().Be(LifetimeWord.Stopped);

        // A person writes the word by hand before a deploy, so a line end after it is the same word.
        Lifetime.Read("replacing\r\n").Should().Be(LifetimeWord.Replacing);
    }

    [TestMethod]
    public void A_file_that_is_missing_or_says_anything_else_reads_as_running()
    {
        // A desktop that left no word did not leave on purpose.
        Lifetime.Read(null).Should().Be(LifetimeWord.Running);
        Lifetime.Read("").Should().Be(LifetimeWord.Running);
        Lifetime.Read("Stopped").Should().Be(LifetimeWord.Running);
        Lifetime.Read("replacing soon").Should().Be(LifetimeWord.Running);
    }

    [TestMethod]
    public void A_desktop_that_exits_under_running_crashed_so_its_windows_are_shown_at_once_and_it_is_started_again()
    {
        Lifetime.AfterExit(LifetimeWord.Running, arrived: false, TimeSpan.Zero).Should().Be(ExitStep.ShowAndRestart);
    }

    [TestMethod]
    public void A_desktop_that_exits_under_stopped_has_what_it_still_hid_shown_and_stays_down()
    {
        Lifetime.AfterExit(LifetimeWord.Stopped, arrived: false, TimeSpan.Zero).Should().Be(ExitStep.ShowAndStayDown);
    }

    [TestMethod]
    public void A_desktop_that_exits_under_replacing_is_waited_for_and_nothing_is_shown_meanwhile()
    {
        Lifetime.AfterExit(LifetimeWord.Replacing, arrived: false, TimeSpan.Zero).Should().Be(ExitStep.Wait);
        Lifetime.AfterExit(LifetimeWord.Replacing, arrived: false, TimeSpan.FromSeconds(29.9)).Should().Be(ExitStep.Wait);
    }

    [TestMethod]
    public void A_replacing_with_no_desktop_after_thirty_seconds_has_its_windows_shown_and_stays_down()
    {
        Lifetime.ReplaceLimit.Should().Be(TimeSpan.FromSeconds(30));

        Lifetime.AfterExit(LifetimeWord.Replacing, arrived: false, TimeSpan.FromSeconds(30)).Should().Be(ExitStep.ShowAndStayDown);
        Lifetime.AfterExit(LifetimeWord.Replacing, arrived: false, TimeSpan.FromMinutes(5)).Should().Be(ExitStep.ShowAndStayDown);
    }

    [TestMethod]
    public void A_desktop_that_arrived_is_left_everything_whatever_the_word()
    {
        // What the old desktop left is the new one's now: its hidden windows, the taskbar it hides again, and its own watcher.
        Lifetime.AfterExit(LifetimeWord.Replacing, arrived: true, TimeSpan.FromSeconds(1)).Should().Be(ExitStep.Leave);
        Lifetime.AfterExit(LifetimeWord.Replacing, arrived: true, TimeSpan.FromMinutes(5)).Should().Be(ExitStep.Leave);

        // A restart starts the new desktop itself, and it may have written running before the old one's watcher woke.
        Lifetime.AfterExit(LifetimeWord.Running, arrived: true, TimeSpan.Zero).Should().Be(ExitStep.Leave);
        Lifetime.AfterExit(LifetimeWord.Stopped, arrived: true, TimeSpan.Zero).Should().Be(ExitStep.Leave);
    }

    [TestMethod]
    public void A_file_another_program_holds_is_asked_for_five_more_times_inside_a_third_of_a_second()
    {
        var waits = new List<TimeSpan>();
        for (var failed = 1; Lifetime.RetryAfter(failed) is TimeSpan wait; failed++)
        {
            waits.Add(wait);
        }

        // The watcher shows a crashed desktop's windows within a second, and the desktop's thread makes the write as it starts.
        waits.Select(wait => wait.TotalMilliseconds).Should().Equal(10, 20, 40, 80, 160);
        Lifetime.RetryAfter(6).Should().BeNull();
    }

    [TestMethod]
    public void A_word_written_is_the_word_read_and_a_folder_with_no_file_reads_as_running()
    {
        using var folder = new Folder();

        Lifetime.TryRead(folder.File, out var none, out _).Should().BeTrue();
        none.Should().Be(LifetimeWord.Running);

        Lifetime.TryWrite(folder.File, LifetimeWord.Replacing, out _).Should().BeTrue();
        Lifetime.TryRead(folder.File, out var word, out _).Should().BeTrue();
        word.Should().Be(LifetimeWord.Replacing);

        // A shorter word leaves nothing of the longer one behind.
        Lifetime.TryWrite(folder.File, LifetimeWord.Running, out _).Should().BeTrue();
        File.ReadAllText(folder.File).Should().Be("running");
    }

    [TestMethod]
    public void A_reader_and_a_writer_have_the_file_at_the_same_moment()
    {
        using var folder = new Folder();
        Lifetime.TryWrite(folder.File, LifetimeWord.Replacing, out _);

        // The watcher of a replaced desktop is reading as the next desktop writes.
        using (new FileStream(folder.File, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            Lifetime.TryWrite(folder.File, LifetimeWord.Running, out _, Refuse).Should().BeTrue();
        }

        using (new FileStream(folder.File, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            Lifetime.TryRead(folder.File, out var word, out _, Refuse).Should().BeTrue();
            word.Should().Be(LifetimeWord.Running);
        }
    }

    [TestMethod]
    public void A_write_that_meets_a_program_holding_the_file_lands_once_that_program_lets_go()
    {
        using var folder = new Folder();
        Lifetime.TryWrite(folder.File, LifetimeWord.Replacing, out _);

        // A program that reads the file the plain way lets no one write meanwhile, as the watcher of an older build does.
        var holder = new FileStream(folder.File, FileMode.Open, FileAccess.Read, FileShare.Read);
        var waits = new List<TimeSpan>();

        var written = Lifetime.TryWrite(folder.File, LifetimeWord.Running, out var error, wait =>
        {
            waits.Add(wait);
            if (waits.Count == 2)
            {
                holder.Dispose();
            }
        });

        written.Should().BeTrue();
        error.Should().BeNull();
        waits.Select(wait => wait.TotalMilliseconds).Should().Equal(10, 20);
        File.ReadAllText(folder.File).Should().Be("running");
    }

    [TestMethod]
    public void A_file_that_stays_held_is_given_up_on_and_the_word_it_holds_stands()
    {
        using var folder = new Folder();
        Lifetime.TryWrite(folder.File, LifetimeWord.Replacing, out _);
        var waits = 0;

        using (new FileStream(folder.File, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Lifetime.TryWrite(folder.File, LifetimeWord.Running, out var unwritten, _ => waits++).Should().BeFalse();
            unwritten.Should().BeOfType<IOException>();
            waits.Should().Be(5);

            // A word that could not be read is running: a desktop whose word is not known is shown and started as after a crash.
            Lifetime.TryRead(folder.File, out var word, out var unread, _ => waits++).Should().BeFalse();
            word.Should().Be(LifetimeWord.Running);
            unread.Should().BeOfType<IOException>();
            waits.Should().Be(10);
        }

        File.ReadAllText(folder.File).Should().Be("replacing");
    }

    private static void Refuse(TimeSpan wait) => throw new InvalidOperationException("the file was refused, and a wait of " + wait + " was asked for");

    [TestMethod]
    public void A_desktop_that_starts_keeps_hidden_what_a_replaced_one_left_and_shows_what_any_other_left()
    {
        Lifetime.TakesOverHidden(LifetimeWord.Replacing).Should().BeTrue();

        // After a crash the watcher shows them, and after a Stop the desktop did. A record that is still there is shown.
        Lifetime.TakesOverHidden(LifetimeWord.Running).Should().BeFalse();
        Lifetime.TakesOverHidden(LifetimeWord.Stopped).Should().BeFalse();
    }

    /// <summary>A person's folder that does not exist yet, and the <c>lifetime</c> file in it. Removed with the test.</summary>
    private sealed class Folder : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "winoma-lifetime-" + Guid.NewGuid().ToString("n"));

        public string File => Path.Combine(_path, Lifetime.FileName);

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }
}

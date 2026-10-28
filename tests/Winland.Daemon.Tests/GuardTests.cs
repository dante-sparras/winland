namespace Winland.Daemon.Tests;

[Collection("Console")]
public class GuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARunFailureIsPrintedAndRethrownAfterCleanup(bool invalidOperation)
    {
        var stderr = Console.Error;
        using var log = new StringWriter();
        Console.SetError(log);
        var cleaned = false;
        Exception failure = invalidOperation ? new InvalidOperationException("loop failed") : new IOException("loop failed");
        try
        {
            var thrown = Assert.ThrowsAny<Exception>(() => Program.Guard(() =>
            {
                try
                {
                    throw failure;
                }
                finally
                {
                    cleaned = true;
                }
            }, rethrow: true));

            Assert.Same(failure, thrown);
            Assert.True(cleaned);
            Assert.Contains(failure.GetType().FullName + ": loop failed", log.ToString(), StringComparison.Ordinal);
            Assert.Contains(nameof(ARunFailureIsPrintedAndRethrownAfterCleanup), log.ToString(), StringComparison.Ordinal);
            Assert.Contains("   at ", log.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(stderr);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpectedInstallFailuresStayPlainMessages(bool argument)
    {
        var stderr = Console.Error;
        using var log = new StringWriter();
        Console.SetError(log);
        try
        {
            Assert.Equal(1, Program.Guard(() => throw (argument ? new ArgumentException("install failed") : new InvalidOperationException("install failed"))));
            Assert.Equal("install failed" + Environment.NewLine, log.ToString());
        }
        finally
        {
            Console.SetError(stderr);
        }
    }
}

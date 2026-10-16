using Winland.Install;

namespace Winland.Install.Tests;

public class RelaunchPlanTests
{
    [Fact]
    public void RelaunchSetsDotnetRootFromTheRuntimeDirectoryAndKeepsTheWorkingDirectory()
    {
        var spec = RelaunchPlan.Create(
            @"C:\Tools\winland.exe",
            ["--winland-elevated", "--config", @"C:\temp\winland.conf"],
            @"C:\temp\session",
            runtimeDirectory: @"C:\Users\dante\AppData\Local\mise\dotnet-root\shared\Microsoft.NETCore.App\10.0.12\",
            dotnetRoot: null,
            pipe: "winland-hand",
            state: @"C:\temp\session\state.json",
            interpreter: @"C:\Windows\System32\cmd.exe");

        Assert.Equal(@"C:\Windows\System32\cmd.exe", spec.FileName);
        Assert.Equal(@"C:\temp\session", spec.WorkingDirectory);
        Assert.Equal(
            "/d /v:off /s /c \"cd /d \"C:\\temp\\session\" && set \"DOTNET_ROOT=C:\\Users\\dante\\AppData\\Local\\mise\\dotnet-root\" && set \"WINLAND_PIPE=winland-hand\" && set \"WINLAND_STATE=C:\\temp\\session\\state.json\" && \"C:\\Tools\\winland.exe\" --winland-elevated --config C:\\temp\\winland.conf\"",
            spec.Arguments);
    }

    [Fact]
    public void ExplicitDotnetRootWinsOverTheRuntimeDirectory()
    {
        var spec = RelaunchPlan.Create(
            @"C:\Tools\winland.exe",
            ["install", @"DESKTOP\dante"],
            @"C:\Tools",
            runtimeDirectory: @"C:\Users\dante\AppData\Local\mise\dotnet-root\shared\Microsoft.NETCore.App\10.0.12\",
            dotnetRoot: @"D:\dotnet sdk",
            pipe: null,
            state: null,
            interpreter: @"C:\Windows\System32\cmd.exe");

        Assert.Equal(
            "/d /v:off /s /c \"cd /d \"C:\\Tools\" && set \"DOTNET_ROOT=D:\\dotnet sdk\" && \"C:\\Tools\\winland.exe\" install DESKTOP\\dante\"",
            spec.Arguments);
    }

    [Fact]
    public void RelaunchQuotesPathsThatContainSpaces()
    {
        var spec = RelaunchPlan.Create(
            @"C:\Program Files\Winland\winland.exe",
            ["--config", @"C:\My Config\winland.conf"],
            @"C:\My Config",
            runtimeDirectory: null,
            dotnetRoot: @"D:\dotnet",
            pipe: null,
            state: @"C:\My Config\state.json",
            interpreter: @"C:\Windows\System32\cmd.exe");

        Assert.Equal(@"C:\My Config", spec.WorkingDirectory);
        Assert.Equal(
            "/d /v:off /s /c \"cd /d \"C:\\My Config\" && set \"DOTNET_ROOT=D:\\dotnet\" && set \"WINLAND_STATE=C:\\My Config\\state.json\" && \"C:\\Program Files\\Winland\\winland.exe\" --config \"C:\\My Config\\winland.conf\"\"",
            spec.Arguments);
    }

    // A backslash before a closing quote escapes the quote, so the argument would swallow
    // everything after it. Doubling the trailing backslashes keeps the argument whole.
    [Fact]
    public void TrailingBackslashesAreDoubledBeforeTheClosingQuote()
    {
        var spec = RelaunchPlan.Create(
            @"C:\Tools\winland.exe",
            ["--config", @"C:\My Config\", "next"],
            @"C:\work",
            runtimeDirectory: null,
            dotnetRoot: null,
            pipe: null,
            state: null,
            interpreter: null);

        Assert.EndsWith(" --config \"C:\\My Config\\\\\" next\"", spec.Arguments, StringComparison.Ordinal);
    }

    // cmd expands %NAME% even inside quotes, and a quote ends cmd's quoting, which lets the
    // rest of an argument run as another command in the elevated cmd.
    [Theory]
    [InlineData("a\"b")]
    [InlineData("%PATH%")]
    [InlineData("50%")]
    [InlineData("two\nlines")]
    [InlineData("two\rlines")]
    public void AnArgumentThatCmdWouldReadIsRefused(string value)
    {
        Assert.Throws<ArgumentException>(() => Plan(@"C:\Tools\winland.exe", [value], @"C:\work", null, null));
        Assert.Throws<ArgumentException>(() => Plan(@"C:\Tools\" + value + ".exe", [], @"C:\work", null, null));
        Assert.Throws<ArgumentException>(() => Plan(@"C:\Tools\winland.exe", [], @"C:\work\" + value, null, null));
        Assert.Throws<ArgumentException>(() => Plan(@"C:\Tools\winland.exe", [], @"C:\work", value, null));
        Assert.Throws<ArgumentException>(() => Plan(@"C:\Tools\winland.exe", [], @"C:\work", null, value));
    }

    [Fact]
    public void SpecialCharactersThatAreSafeInsideQuotesAreQuoted()
    {
        var spec = Plan(@"C:\Tools\winland.exe", ["a&b", "c|d", "e^f", "g!h"], @"C:\work", null, null);

        Assert.EndsWith(" \"a&b\" \"c|d\" \"e^f\" \"g!h\"\"", spec.Arguments, StringComparison.Ordinal);
    }

    private static RelaunchSpec Plan(string executable, string[] arguments, string workingDirectory, string? pipe, string? state) =>
        RelaunchPlan.Create(executable, arguments, workingDirectory, runtimeDirectory: null, dotnetRoot: null, pipe, state, interpreter: null);

    [Fact]
    public void RelaunchWithoutARuntimeStillEntersTheWorkingDirectory()
    {
        var spec = RelaunchPlan.Create(
            @"C:\Tools\winland.exe",
            ["uninstall"],
            @"C:\work",
            runtimeDirectory: null,
            dotnetRoot: null,
            pipe: null,
            state: " ",
            interpreter: null);

        Assert.Equal("cmd.exe", spec.FileName);
        Assert.Equal(
            "/d /v:off /s /c \"cd /d \"C:\\work\" && \"C:\\Tools\\winland.exe\" uninstall\"",
            spec.Arguments);
        Assert.Equal(@"C:\work", spec.WorkingDirectory);
    }
}

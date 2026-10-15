using Winland;

namespace Winland.Tests;

public class ConfigSyntaxTests
{
    [Fact]
    public void ConditionalKeepsTheBranchWhoseVariableExists()
    {
        var text = """
            $MY_VAR = 1
            # hyprlang if MY_VAR
            general:gaps_in = 5
            # hyprlang endif
            # hyprlang if !MY_VAR
            general:gaps_out = 9
            # hyprlang endif
            """;

        var document = ConfigParser.Parse(text);

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "5"), Assert.Single(document.Settings));
    }

    [Fact]
    public void EmptyVariableIsFalseAndAMissingOneIsFalse()
    {
        var empty = ConfigParser.Parse("""
            $MY_VAR =
            # hyprlang if MY_VAR
            general:gaps_in = 5
            # hyprlang endif
            # hyprlang if !MY_VAR
            general:gaps_out = 9
            # hyprlang endif
            """);
        var missing = ConfigParser.Parse("""
            # hyprlang if MISSING
            general:gaps_in = 5
            # hyprlang endif
            # hyprlang if !MISSING
            general:gaps_out = 9
            # hyprlang endif
            """);

        Assert.Empty(empty.Warnings);
        Assert.Equal(new Setting("general:gaps_out", "9"), Assert.Single(empty.Settings));
        Assert.Empty(missing.Warnings);
        Assert.Equal(new Setting("general:gaps_out", "9"), Assert.Single(missing.Settings));
    }

    [Fact]
    public void ZeroIsTruthyBecauseTheVariableIsNotEmpty()
    {
        var document = ConfigParser.Parse("""
            $MY_VAR = 0
            # hyprlang if MY_VAR
            general:gaps_in = 5
            # hyprlang endif
            """);

        Assert.Equal(new Setting("general:gaps_in", "5"), Assert.Single(document.Settings));
    }

    [Fact]
    public void EnvironmentVariableFeedsTheConditional()
    {
        var name = "WINLAND_IF_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "set");
        try
        {
            var document = ConfigParser.Parse($"""
                # hyprlang if {name}
                general:gaps_in = 5
                # hyprlang endif
                # hyprlang if !{name}
                general:gaps_out = 9
                # hyprlang endif
                """);

            Assert.Empty(document.Warnings);
            Assert.Equal(new Setting("general:gaps_in", "5"), Assert.Single(document.Settings));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void EnvironmentVariableWinsOverAConfigVariableInAConditional()
    {
        var name = "WINLAND_IF_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, "");
        try
        {
            var document = ConfigParser.Parse($"""
                ${name} = present
                # hyprlang if {name}
                general:gaps_in = 5
                # hyprlang endif
                """);

            Assert.Empty(document.Settings);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void FailedConditionalStillClosesAndLaterLinesRun()
    {
        var document = ConfigParser.Parse("""
            # hyprlang if MISSING
            general:gaps_in = 1
            # hyprlang endif
            general:gaps_out = 2
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_out", "2"), Assert.Single(document.Settings));
    }

    [Fact]
    public void NestedConditionalRequiresEveryEnclosingTest()
    {
        var document = ConfigParser.Parse("""
            $INNER = 1
            # hyprlang if MISSING
            # hyprlang if INNER
            general:gaps_in = 3
            # hyprlang endif
            # hyprlang endif
            general:gaps_out = 4
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_out", "4"), Assert.Single(document.Settings));
    }

    [Fact]
    public void StrayEndifWarns()
    {
        var document = ConfigParser.Parse("""
            # hyprlang endif
            general:gaps_in = 1
            """);

        Assert.Equal(new ConfigWarning(1, "Stray endif."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "1"), Assert.Single(document.Settings));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("enable")]
    [InlineData("enabled")]
    [InlineData("set")]
    public void NoErrorSuppressesWarningsUntilTurnedOff(string word)
    {
        var document = ConfigParser.Parse($"""
            # hyprlang noerror {word}
            ghostkeyword = 1
            not a line
            # hyprlang noerror false
            ghostkeyword = 2
            """);

        Assert.Equal(new ConfigWarning(5, "Unsupported keyword 'ghostkeyword'."), Assert.Single(document.Warnings));
        Assert.Empty(document.Settings);
    }

    [Fact]
    public void ExpressionReplacesASumProductAndQuotient()
    {
        var document = ConfigParser.Parse("""
            $VAR1 = 2
            $VAR2 = {{VAR1 + 3}}
            general:gaps_in = {{VAR2 * 2}}
            general:border_size = {{10 / 4}}
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[]
            {
                new Setting("general:gaps_in", "10"),
                new Setting("general:border_size", "2.5"),
            },
            document.Settings);
    }

    [Fact]
    public void ExpressionWithoutSpacesIsRejected()
    {
        var document = ConfigParser.Parse("""
            general:gaps_in = {{2+3}}
            general:gaps_out = 4
            """);

        Assert.Equal(new ConfigWarning(1, "Invalid expression."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_out", "4"), Assert.Single(document.Settings));
    }

    [Fact]
    public void EscapedExpressionStaysLiteralAndADoubledBackslashEvaluates()
    {
        var document = ConfigParser.Parse("""
            $VAR1 = 2
            exec = \{{10 + 10}}
            exec = \\{{VAR1 + 10}}
            exec = \\{ hello \\}
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[]
            {
                new Setting("exec", "{{10 + 10}}"),
                new Setting("exec", @"\12"),
                new Setting("exec", @"\{ hello \}"),
            },
            document.Settings);
    }

    [Fact]
    public void SourceExpandsTildeAndEnvironmentVariables()
    {
        var name = "WINLAND_SRC_" + Guid.NewGuid().ToString("N");
        var previousHome = Environment.GetEnvironmentVariable("HOME");
        Environment.SetEnvironmentVariable("HOME", @"C:\winland-home");
        Environment.SetEnvironmentVariable(name, "colors.conf");
        try
        {
            var source = new MemorySource(new Dictionary<string, string>
            {
                [@"C:\winland-home/colors.conf"] = "general:gaps_in = 4\n",
                ["colors.conf"] = "general:gaps_out = 8\n",
            });

            var document = ConfigParser.Parse($"source = ~/colors.conf\nsource = ${name}\n", source);

            Assert.Empty(document.Warnings);
            Assert.Equal(
                new[]
                {
                    new Setting("general:gaps_in", "4"),
                    new Setting("general:gaps_out", "8"),
                },
                document.Settings);
            Assert.Equal([@"C:\winland-home/colors.conf", "colors.conf"], document.Sources);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void SourceGlobReadsEachMatch()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            ["a.conf"] = "general:gaps_in = 1\n",
            ["b.conf"] = "general:gaps_out = 2\n",
            ["c.txt"] = "general:border_size = 9\n",
        });

        var document = ConfigParser.Parse("source = *.conf\n", source);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[]
            {
                new Setting("general:gaps_in", "1"),
                new Setting("general:gaps_out", "2"),
            },
            document.Settings);
    }

    [Fact]
    public void SourceGlobWithNoMatchWarns()
    {
        var document = ConfigParser.Parse("source = *.missing\ngeneral:gaps_in = 2\n", new MemorySource(new Dictionary<string, string>()));

        Assert.Equal(new ConfigWarning(1, "Could not read source '*.missing'."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "2"), Assert.Single(document.Settings));
    }

    [Fact]
    public void KeyedBlockDoesNotLeakItsLines()
    {
        var document = ConfigParser.Parse("""
            device[mouse] {
                sensitivity = 1
                gaps_in = 9
            }
            general:gaps_in = 2
            """);

        Assert.Equal(
            new[]
            {
                new ConfigWarning(2, "Unsupported keyword 'device:sensitivity'."),
                new ConfigWarning(3, "Unsupported keyword 'device:gaps_in'."),
            },
            document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "2"), Assert.Single(document.Settings));
    }

    [Fact]
    public void InlineKeyedDeviceLineWarnsAsAGapAndDoesNotJoinTheEnclosingCategory()
    {
        var document = ConfigParser.Parse("""
            general {
                device[mouse]:sensitivity = 1
                gaps_in = 2
            }
            """);

        Assert.Equal(new ConfigWarning(2, "Unsupported keyword 'device:sensitivity'."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "2"), Assert.Single(document.Settings));
    }

    [Fact]
    public void EmptyConfigUsesTheSourceDefaults()
    {
        var document = ConfigParser.Parse("");
        var layout = LayoutOptions.From(document);
        var decoration = DecorationOptions.From(document);

        Assert.Empty(document.Warnings);
        Assert.Equal(EdgeInsets.Uniform(5), layout.GapsIn);
        Assert.Equal(EdgeInsets.Uniform(20), layout.GapsOut);
        Assert.Equal(1, layout.BorderSize);
        Assert.Equal(0, layout.ForceSplit);
        Assert.False(layout.PreserveSplit);
        Assert.False(layout.SmartSplit);
        Assert.False(layout.PermanentDirectionOverride);
        Assert.Equal(1, layout.SplitWidthMultiplier);
        Assert.True(layout.UseActiveForSplits);
        Assert.Equal(1, layout.DefaultSplitRatio);
        Assert.Equal(0, layout.SplitBias);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), decoration.ActiveBorder);
        Assert.Equal(new BorderColor(0xFF, 0x44, 0x44, 0x44), decoration.InactiveBorder);
        Assert.Equal(CornerPreference.DoNotRound, decoration.Corners);
    }

    private sealed class MemorySource(IReadOnlyDictionary<string, string> files) : IConfigSource
    {
        public string? Read(string path) => files.TryGetValue(path, out var text) ? text : null;

        public IReadOnlyList<string> Match(string pattern) =>
            files.Keys.Where(path => SourcePath.Matches(pattern, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}

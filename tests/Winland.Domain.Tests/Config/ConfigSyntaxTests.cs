namespace Winland.Domain.Tests;

public class ConfigSyntaxTests
{
    [Theory]
    [InlineData("layers")]
    [InlineData("layersIn")]
    [InlineData("layersOut")]
    [InlineData("fadeLayersIn")]
    [InlineData("fadeLayersOut")]
    [InlineData("zoomFactor")]
    public void AnimationNodesWinlandDoesNotDrawExplainThat(string name)
    {
        var document = ConfigParser.Parse($"animation = {name}, 1, 3, default\n");

        Assert.Equal($"Animation '{name}' is not one Winland draws.", Assert.Single(document.Warnings).Message);
    }

    [Fact]
    public void AnimationOptionsAcceptABooleanPrefix()
    {
        var document = ConfigParser.Parse("animations {\nenabled = yes, please :)\n}\n");

        Assert.Empty(document.Warnings);
        Assert.True(AnimationOptions.From(document).Enabled);
    }

    [Fact]
    public void AnEscapedHashAtTheStartStillCommentsOutATrailingBackslash()
    {
        var document = ConfigParser.Parse("  ## path\\\ngeneral:gaps_in = 4\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "4"), Assert.Single(document.Settings));
    }

    [Fact]
    public void SubmapBindsAreSkippedUntilResetWithOneWarning()
    {
        var document = ConfigParser.Parse("submap = resize\nbind = , H, movefocus, l\nbind = , L, movefocus, r\nsubmap = other\nbind = , Q, exit\nsubmap = reset\nbind = SUPER, Q, killactive\n");

        Assert.Contains("submaps", Assert.Single(document.Warnings).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("killactive", Assert.IsType<Bind>(Assert.Single(document.Binds)).Dispatcher);
    }

    [Theory]
    [InlineData("SUPER SHFT")]
    [InlineData("SUPERunknown")]
    public void UnrecognisedLettersInModifiersDropTheBind(string modifiers)
    {
        var document = ConfigParser.Parse($"bind = {modifiers}, Q, killactive\n");

        Assert.Single(document.Warnings);
        Assert.Empty(document.Binds);
    }

    [Theory]
    [InlineData("SUPER_SHIFT")]
    [InlineData("SUPER+SHIFT")]
    [InlineData("SUPER SHIFT")]
    [InlineData("CONTROLALT")]
    public void ModifierNamesStillAllowSeparatorsAndConcatenation(string modifiers)
    {
        var document = ConfigParser.Parse($"bind = {modifiers}, Q, killactive\n");

        Assert.Empty(document.Warnings);
        Assert.Single(document.Binds);
    }

    [Theory]
    [InlineData("t")]
    [InlineData("i")]
    [InlineData("s")]
    [InlineData("o")]
    [InlineData("c")]
    [InlineData("g")]
    [InlineData("u")]
    public void UnsupportedBindFlagsSayTheyAreNotSupported(string flag)
    {
        var document = ConfigParser.Parse($"bind{flag} = SUPER, Q, killactive\n");

        Assert.Contains("not supported", Assert.Single(document.Warnings).Message);
        Assert.Empty(document.Binds);
    }

    [Fact]
    public void TheBypassBindFlagIsAcceptedWithoutChangingTheBind()
    {
        var document = ConfigParser.Parse("bindp = SUPER, Q, killactive\n");

        Assert.Empty(document.Warnings);
        Assert.Equal(BindFlag.None, Assert.IsType<Bind>(Assert.Single(document.Binds)).Flags);
    }

    [Fact]
    public void VariableExpansionThatExceeds64KWarnsAndSkipsTheLine()
    {
        var document = ConfigParser.Parse($"$large = {new string('x', 32768)}\nexec = $large$large$large\ngeneral:gaps_in = 4\n");

        Assert.Single(document.Warnings);
        Assert.DoesNotContain(document.Settings, setting => setting.Keyword == "exec");
        Assert.Contains(new Setting("general:gaps_in", "4"), document.Settings);
    }

    [Fact]
    public void ADoublingVariableWarnsWithoutExhaustingMemory()
    {
        var document = ConfigParser.Parse("$a = $a$a\nexec = $a\ngeneral:gaps_in = 4\n");

        Assert.Single(document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "4"), Assert.Single(document.Settings));
    }

    [Fact]
    public void AnIfCommentIsOnlyAComment()
    {
        var document = ConfigParser.Parse("""
            # hyprlang if MISSING
            general:gaps_in = 3
            # hyprlang endif
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "3"), Assert.Single(document.Settings));
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
            scrolling:column_width = {{1 / 4}}
            """);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[]
            {
                new Setting("general:gaps_in", "10"),
                new Setting("scrolling:column_width", "0.25"),
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
        var source = new MemorySource(
            new Dictionary<string, string>
            {
                [@"C:\winland-home\colors.conf"] = "general:gaps_in = 4\n",
                ["colors.conf"] = "general:gaps_out = 8\n",
            },
            new Dictionary<string, string>
            {
                ["HOME"] = @"C:\winland-home",
                ["WINLAND_SRC"] = "colors.conf",
            });

        var document = ConfigParser.Parse("source = ~/colors.conf\nsource = $WINLAND_SRC\n", source);

        Assert.Empty(document.Warnings);
        Assert.Equal(
            new[]
            {
                new Setting("general:gaps_in", "4"),
                new Setting("general:gaps_out", "8"),
            },
            document.Settings);
        Assert.Equal([@"C:\winland-home\colors.conf", "colors.conf"], document.Sources);
    }

    [Fact]
    public void AStrayBraceInASourcedFileDoesNotCloseTheCategoryAroundIt()
    {
        var source = new MemorySource(new Dictionary<string, string> { ["a.conf"] = "}\n" });

        var document = ConfigParser.Parse("general {\nsource = a.conf\ngaps_in = 7\n}\n", source);

        Assert.Contains(new Setting("general:gaps_in", "7"), document.Settings);
        Assert.Contains(document.Warnings, warning => warning.Source == "a.conf");
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
    public void SourceGlobInTheConfigFolderDoesNotReadTheConfigAgain()
    {
        const string root = "bind = SUPER, Q, killactive\nsource = C:/cfg/*.conf\n";
        var source = new MemorySource(new Dictionary<string, string>
        {
            [@"C:\cfg\colors.conf"] = "general:gaps_in = 1\n",
            [@"C:\cfg\winland.conf"] = root,
        });

        var document = ConfigParser.Parse(root, source, path: @"C:\cfg\winland.conf");

        Assert.Empty(document.Warnings);
        Assert.Single(document.Binds);
        Assert.Equal(new Setting("general:gaps_in", "1"), Assert.Single(document.Settings));
    }

    [Theory]
    [InlineData("winland.conf")]
    [InlineData(@"C:\cfg\winland.conf")]
    [InlineData("C:/CFG/Winland.conf")]
    [InlineData(@"C:\cfg\sub\..\winland.conf")]
    public void SourceNamingTheConfigItselfWarns(string named)
    {
        var root = $"bind = SUPER, Q, killactive\nsource = {named}\n";
        var source = new MemorySource(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(Path.Combine(@"C:\cfg", named))] = root,
        });

        var document = ConfigParser.Parse(root, source, path: @"C:\cfg\winland.conf");

        Assert.Equal(new ConfigWarning(2, $"Source '{Path.GetFullPath(Path.Combine(@"C:\cfg", named))}' includes itself."), Assert.Single(document.Warnings));
        Assert.Single(document.Binds);
    }

    [Fact]
    public void SourceCycleIsFoundWhateverTheCaseOfThePath()
    {
        var source = new MemorySource(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a.conf"] = "source = B.CONF\n",
            ["b.conf"] = "source = A.conf\ngeneral:gaps_in = 3\n",
        });

        var document = ConfigParser.Parse("source = a.conf\n", source);

        Assert.Equal(new ConfigWarning(1, "Source 'A.conf' includes itself.", "B.CONF"), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "3"), Assert.Single(document.Settings));
    }

    [Fact]
    public void SourceGlobWithNoMatchWarns()
    {
        var document = ConfigParser.Parse("source = *.missing\ngeneral:gaps_in = 2\n", new MemorySource(new Dictionary<string, string>()));

        Assert.Equal(new ConfigWarning(1, "Could not read source '*.missing'."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "2"), Assert.Single(document.Settings));
        Assert.Equal(["*.missing"], document.Sources);
    }

    [Fact]
    public void NestedSourcesResolveAgainstTheirOwnFileAndWarningsNameItsFullPath()
    {
        var source = new MemorySource(new Dictionary<string, string>
        {
            [@"C:\cfg\parts\extra.conf"] = "source = colors.conf\n",
            [@"C:\cfg\parts\colors.conf"] = "general:gaps_in = 4\nunknown = 1\n",
        });

        var document = ConfigParser.Parse("source = parts/extra.conf\n", source, path: @"C:\cfg\winland.conf");

        Assert.Equal(new Setting("general:gaps_in", "4"), Assert.Single(document.Settings));
        Assert.Equal(@"C:\cfg\parts\colors.conf", Assert.Single(document.Warnings).Source);
        Assert.Equal([@"C:\cfg\parts\extra.conf", @"C:\cfg\parts\colors.conf"], document.Sources);
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
            [
                new ConfigWarning(2, "Unsupported keyword 'device[mouse]:sensitivity'."),
                new ConfigWarning(3, "Unsupported keyword 'device[mouse]:gaps_in'."),
            ],
            document.Warnings);
        Assert.Equal(new Setting("general:gaps_in", "2"), Assert.Single(document.Settings));
    }

    [Fact]
    public void InlineKeyedDeviceLineIsKeptAndDoesNotJoinTheEnclosingCategory()
    {
        var document = ConfigParser.Parse("""
            general {
                device[mouse]:sensitivity = 1
                gaps_in = 2
            }
            """);

        Assert.Equal(new ConfigWarning(2, "Unsupported keyword 'device[mouse]:sensitivity'."), Assert.Single(document.Warnings));
        Assert.Equal(new Setting("general:gaps_in", "2"), Assert.Single(document.Settings));
    }

    [Fact]
    public void EmptyConfigUsesTheSourceDefaults()
    {
        var document = ConfigParser.Parse("");
        var layout = LayoutOptions.From(document);
        var decoration = DecorationOptions.From(document);

        Assert.Empty(document.Warnings);
        Assert.Equal(EdgeInsets.Uniform(4), layout.GapsIn);
        Assert.Equal(EdgeInsets.Uniform(8), layout.GapsOut);
        Assert.Equal(2, layout.BorderSize);
        Assert.False(layout.PreserveSplit);
        Assert.Equal(new BorderColor(0xFF, 0xFF, 0xFF, 0xFF), decoration.ActiveBorder);
        Assert.Equal(new BorderColor(0xFF, 0x40, 0x40, 0x40), decoration.InactiveBorder);
        Assert.Equal(8, decoration.Rounding);
        Assert.True(decoration.Blur);
    }

    private sealed class MemorySource(IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, string>? variables = null) : IConfigSource
    {
        public string? Read(string path) => files.TryGetValue(path, out var text) ? text : null;

        public string? Variable(string name) => variables is not null && variables.TryGetValue(name, out var value) ? value : null;

        public IReadOnlyList<string> Match(string pattern) =>
            files.Keys.Where(path => SourcePath.Matches(pattern, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}

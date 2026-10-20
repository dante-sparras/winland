using Winland;

namespace Winland.Tests;

public class XrayBlurTests
{
    private static readonly Rectangle Area = new(0, 0, 800, 600);

    private static readonly BlurTuning Plain = new()
    {
        Contrast = 1f,
        Brightness = 1f,
        Noise = 0f,
        Vibrancy = 0f,
    };

    [Theory]
    [InlineData(1, 8)]
    [InlineData(3, 4)]
    [InlineData(8, 20)]
    public void AFlatPictureStaysFlatThroughEveryPass(int passes, int size)
    {
        var picture = Fill(13, 9, 0xFF6496C8);

        var blurred = BlurPipeline.Blur(picture, 13, 9, Plain with { Passes = passes, Size = size });

        Assert.Equal(13 * 9, blurred.Length);
        Assert.All(blurred, pixel => Assert.Equal(0xFF6496C8u, pixel));
    }

    [Fact]
    public void ABrightPixelSpreadsIntoItsNeighbors()
    {
        var picture = Fill(16, 16, 0xFF000000);
        picture[(8 * 16) + 8] = 0xFFFFFFFF;

        var blurred = BlurPipeline.Blur(picture, 16, 16, Plain with { Passes = 2, Size = 2 });

        Assert.True(Red(blurred[(8 * 16) + 8]) < 255);
        Assert.True(Red(blurred[(8 * 16) + 9]) > 0);
        Assert.True(Red(blurred[(8 * 16) + 8]) > Red(blurred[(8 * 16) + 14]));
        Assert.All(blurred, pixel => Assert.Equal(0xFFu, pixel >> 24));
    }

    [Fact]
    public void MorePassesSpreadFurther()
    {
        var picture = Fill(32, 32, 0xFF000000);
        picture[(16 * 32) + 16] = 0xFFFFFFFF;

        var one = BlurPipeline.Blur(picture, 32, 32, Plain with { Passes = 1, Size = 1 });
        var three = BlurPipeline.Blur(picture, 32, 32, Plain with { Passes = 3, Size = 1 });

        Assert.True(Red(three[(16 * 32) + 16]) < Red(one[(16 * 32) + 16]));
        Assert.True(Red(three[(16 * 32) + 22]) > Red(one[(16 * 32) + 22]));
    }

    [Fact]
    public void PassesAreUsedFromOneToEightAndSizeZeroStillBlurs()
    {
        var picture = Fill(3, 3, 0xFF202020);
        picture[4] = 0xFFFFFFFF;

        var zeroPasses = BlurPipeline.Blur(picture, 3, 3, Plain with { Passes = 0, Size = 0 });
        var onePass = BlurPipeline.Blur(picture, 3, 3, Plain with { Passes = 1, Size = 0 });
        var tooMany = BlurPipeline.Blur(picture, 3, 3, Plain with { Passes = 10, Size = 100 });

        Assert.Equal(onePass, zeroPasses);
        Assert.Equal(9, tooMany.Length);
        Assert.True(Red(onePass[4]) < 255);
    }

    [Fact]
    public void GainCurvesContrastThroughTheMiddle()
    {
        Assert.Equal(0.5f, BlurPipeline.Gain(0.5f, 0.8916f), 5);
        Assert.Equal(0.125f, BlurPipeline.Gain(0.25f, 2f), 5);
        Assert.Equal(0.875f, BlurPipeline.Gain(0.75f, 2f), 5);
        Assert.Equal(0.3f, BlurPipeline.Gain(0.3f, 1f), 5);
        Assert.Equal(0f, BlurPipeline.Gain(-1f, 2f), 5);
        Assert.Equal(1f, BlurPipeline.Gain(2f, 2f), 5);
    }

    [Fact]
    public void ContrastBelowOneLiftsDarksAndLowersLights()
    {
        var dark = BlurPipeline.Blur(Fill(4, 4, 0xFF404040), 4, 4, Plain with { Contrast = 0.5f });
        var light = BlurPipeline.Blur(Fill(4, 4, 0xFFC0C0C0), 4, 4, Plain with { Contrast = 0.5f });

        Assert.True(Red(dark[0]) > 0x40);
        Assert.True(Red(light[0]) < 0xC0);
    }

    [Fact]
    public void BrightnessAboveOneLiftsThePictureAndBelowOneDarkensTheBlur()
    {
        var gray = Fill(4, 4, 0xFF646464);

        var lifted = BlurPipeline.Blur(gray, 4, 4, Plain with { Brightness = 1.5f });
        var darkened = BlurPipeline.Blur(gray, 4, 4, Plain with { Brightness = 0.5f });
        var clipped = BlurPipeline.Blur(Fill(4, 4, 0xFFC8C8C8), 4, 4, Plain with { Brightness = 2f });

        Assert.Equal(150u, Red(lifted[0]));
        Assert.Equal(50u, Red(darkened[0]));
        Assert.Equal(255u, Red(clipped[0]));
    }

    [Fact]
    public void NoiseMovesEachPixelByLessThanHalfItsAmountAndIsRepeatable()
    {
        var gray = Fill(32, 32, 0xFF808080);
        var tuning = Plain with { Noise = 0.2f };

        var first = BlurPipeline.Blur(gray, 32, 32, tuning);
        var second = BlurPipeline.Blur(gray, 32, 32, tuning);

        Assert.Equal(first, second);
        Assert.True(first.Distinct().Count() > 10);
        Assert.All(first, pixel => Assert.InRange((int)Red(pixel), 128 - 26, 128 + 26));
        Assert.InRange(first.Average(pixel => (double)Red(pixel)), 120, 136);
    }

    [Fact]
    public void HashStaysBetweenZeroAndOneAndVariesWithPlace()
    {
        var values = new List<float>();
        for (var i = 0; i < 50; i++)
            values.Add(BlurPipeline.Hash((i + 0.5f) / 50, 0.3f));

        Assert.All(values, value => Assert.InRange(value, 0f, 1f));
        Assert.True(values.Distinct().Count() > 40);
    }

    [Fact]
    public void VibrancyRaisesTheSaturationOfASaturatedColor()
    {
        var dull = Fill(4, 4, 0xFFDC3C3C);

        var plain = BlurPipeline.Blur(dull, 4, 4, Plain);
        var vibrant = BlurPipeline.Blur(dull, 4, 4, Plain with { Vibrancy = 1f });

        Assert.True(Red(vibrant[0]) - Blue(vibrant[0]) > Red(plain[0]) - Blue(plain[0]));
    }

    [Fact]
    public void VibrancyLeavesGrayAlone()
    {
        var gray = BlurPipeline.Blur(Fill(4, 4, 0xFF808080), 4, 4, Plain with { Vibrancy = 1f });

        Assert.All(gray, pixel => Assert.Equal(0xFF808080u, pixel));
    }

    [Fact]
    public void AnEmptyPictureIsRefused()
    {
        Assert.Throws<ArgumentException>(() => BlurPipeline.Blur([], 0, 0, Plain));
        Assert.Throws<ArgumentException>(() => BlurPipeline.Blur(new uint[3], 2, 2, Plain));
    }

    [Fact]
    public void TheDocumentedOptionsAreTheReleasesDefaults()
    {
        var tuning = BlurTuning.Documented;

        Assert.Equal(8, tuning.Size);
        Assert.Equal(1, tuning.Passes);
        Assert.Equal(0.0117f, tuning.Noise);
        Assert.Equal(0.8916f, tuning.Contrast);
        Assert.Equal(1f, tuning.Brightness);
        Assert.Equal(0.1696f, tuning.Vibrancy);
        Assert.Equal(0f, tuning.VibrancyDarkness);
    }

    [Fact]
    public void StretchFillsTheCanvasWithTheWholePicture()
    {
        uint[] picture = [0xFFFF0000, 0xFF0000FF];

        var fitted = WallpaperFit.Render(picture, 2, 1, WallpaperPosition.Stretch, 0, 8, 2, 0, 0, 8, 2);

        Assert.Equal(0xFFFF0000u, fitted[0]);
        Assert.Equal(0xFF0000FFu, fitted[7]);
        Assert.Equal(fitted[0], fitted[8]);
    }

    [Fact]
    public void CenterLeavesTheBackgroundAroundThePicture()
    {
        uint[] picture = [0xFFFF0000, 0xFF0000FF];

        var fitted = WallpaperFit.Render(picture, 2, 1, WallpaperPosition.Center, 0x00123456, 4, 3, 0, 0, 4, 3);

        Assert.Equal(0xFF123456u, fitted[0]);
        Assert.Equal(0xFFFF0000u, fitted[(1 * 4) + 1]);
        Assert.Equal(0xFF0000FFu, fitted[(1 * 4) + 2]);
        Assert.Equal(0xFF123456u, fitted[(1 * 4) + 3]);
        Assert.Equal(0xFF123456u, fitted[(2 * 4) + 1]);
    }

    [Fact]
    public void FitScalesToTheCanvasAndLetterboxes()
    {
        uint[] picture = [0xFFFF0000, 0xFFFF0000];

        var fitted = WallpaperFit.Render(picture, 2, 1, WallpaperPosition.Fit, 0x00000010, 4, 4, 0, 0, 4, 4);

        Assert.Equal(0xFF000010u, fitted[0]);
        Assert.Equal(0xFFFF0000u, fitted[4]);
        Assert.Equal(0xFFFF0000u, fitted[8 + 3]);
        Assert.Equal(0xFF000010u, fitted[12]);
    }

    [Fact]
    public void FillCoversTheCanvasAndCropsTheOverflow()
    {
        uint[] picture = [0xFF00FF00, 0xFF00FF00];

        var fitted = WallpaperFit.Render(picture, 2, 1, WallpaperPosition.Fill, 0x00FF0000, 2, 2, 0, 0, 2, 2);

        Assert.All(fitted, pixel => Assert.Equal(0xFF00FF00u, pixel));
    }

    [Fact]
    public void TileRepeatsThePictureFromTheTopLeft()
    {
        uint[] picture = [0xFFFF0000, 0xFF0000FF];

        var fitted = WallpaperFit.Render(picture, 2, 1, WallpaperPosition.Tile, 0, 5, 1, 0, 0, 5, 1);

        Assert.Equal([0xFFFF0000u, 0xFF0000FFu, 0xFFFF0000u, 0xFF0000FFu, 0xFFFF0000u], fitted);
    }

    [Fact]
    public void AMonitorShowsItsPartOfTheCanvas()
    {
        uint[] picture = [0xFF000001, 0xFF000002, 0xFF000003, 0xFF000004];

        var fitted = WallpaperFit.Render(picture, 4, 1, WallpaperPosition.Stretch, 0, 4, 1, 2, 0, 2, 1);

        Assert.Equal([0xFF000003u, 0xFF000004u], fitted);
    }

    [Fact]
    public void NoPictureIsTheBackgroundAlone()
    {
        var fitted = WallpaperFit.Render([], 0, 0, WallpaperPosition.Fill, 0x00ABCDEF, 1, 1, 0, 0, 3, 2);

        Assert.Equal(Enumerable.Repeat(0xFFABCDEFu, 6), fitted);
        Assert.Throws<ArgumentOutOfRangeException>(() => WallpaperFit.Render([], 0, 0, WallpaperPosition.Fill, 0, 1, 1, 0, 0, 0, 2));
    }

    [Fact]
    public void TheBoxIsCutFromTheMonitorItCovers()
    {
        var image = new BackdropImage(new Rectangle(10, 20, 4, 4), Enumerable.Range(0, 16).Select(i => 0xFF000000u | (uint)i).ToArray());
        var pixels = new uint[4];

        XrayPainter.Paint(pixels, new Rectangle(11, 21, 2, 2), [image], new XrayBackdrop(BlurTuning.Documented, 0, 2f, 255));

        Assert.Equal([0xFF000005u, 0xFF000006u, 0xFF000009u, 0xFF00000Au], pixels);
    }

    [Fact]
    public void WhatNoMonitorCoversIsLeftClear()
    {
        var image = new BackdropImage(new Rectangle(0, 0, 2, 2), [0xFF111111, 0xFF222222, 0xFF333333, 0xFF444444]);
        var pixels = new uint[9];
        Array.Fill(pixels, 0xDEADBEEFu);

        XrayPainter.Paint(pixels, new Rectangle(1, 1, 3, 3), [image], new XrayBackdrop(BlurTuning.Documented, 0, 2f, 255));

        Assert.Equal(0xFF444444u, pixels[0]);
        Assert.Equal(0u, pixels[1]);
        Assert.Equal(0u, pixels[3]);
        Assert.Equal(0u, pixels[8]);
    }

    [Fact]
    public void ABoxOnTwoMonitorsTakesEachPartFromItsOwnPicture()
    {
        var left = new BackdropImage(new Rectangle(0, 0, 2, 1), [0xFF0000FF, 0xFF0000FF]);
        var right = new BackdropImage(new Rectangle(2, 0, 2, 1), [0xFF00FF00, 0xFF00FF00]);
        var pixels = new uint[4];

        XrayPainter.Paint(pixels, new Rectangle(0, 0, 4, 1), [left, right], new XrayBackdrop(BlurTuning.Documented, 0, 2f, 255));

        Assert.Equal([0xFF0000FFu, 0xFF0000FFu, 0xFF00FF00u, 0xFF00FF00u], pixels);
    }

    [Theory]
    [InlineData(880, 470, 800, 500, 10)]
    [InlineData(20, 20, 2500, 1400, 10)]
    [InlineData(3000, 100, 800, 500, 10)]
    [InlineData(2300, 300, 800, 500, 10)]
    [InlineData(-300, -200, 800, 500, 10)]
    [InlineData(5000, 1300, 800, 500, 10)]
    [InlineData(100, 100, 50, 50, 40)]
    public void AWindowAnywhereOnTwoSideBySideMonitorsIsCutWithoutError(int x, int y, int width, int height, int rounding)
    {
        BackdropImage[] images =
        [
            new(new Rectangle(0, 0, 2560, 1440), Fill(2560, 1440, 0xFF112233)),
            new(new Rectangle(2560, 0, 2560, 1440), Fill(2560, 1440, 0xFF445566)),
        ];
        var box = new Rectangle(x, y, width, height);
        var pixels = new uint[width * height];

        XrayPainter.Paint(pixels, box, images, new XrayBackdrop(BlurTuning.Documented, rounding, 2f, 255));

        var covered = images.Any(image => box.X < image.Area.X + image.Area.Width && box.X + width > image.Area.X
            && box.Y < image.Area.Y + image.Area.Height && box.Y + height > image.Area.Y);
        Assert.Equal(covered, pixels.Any(pixel => pixel != 0));
        for (var row = height / 2; row < (height / 2) + 1; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var screenX = x + column;
                var expected = screenX is >= 0 and < 2560 ? 0xFF112233u : screenX is >= 2560 and < 5120 ? 0xFF445566u : 0u;
                var inRows = y + row is >= 0 and < 1440;
                var corner = column < rounding || column >= width - rounding;
                if (!corner && inRows)
                    Assert.Equal(expected, pixels[(row * width) + column]);
            }
        }
    }

    [Fact]
    public void ALayerAlphaPremultipliesThePicture()
    {
        var image = new BackdropImage(new Rectangle(0, 0, 1, 1), [0xFFC8C8C8]);
        var pixels = new uint[1];

        XrayPainter.Paint(pixels, new Rectangle(0, 0, 1, 1), [image], new XrayBackdrop(BlurTuning.Documented, 0, 2f, 128));

        Assert.Equal(128u, pixels[0] >> 24);
        Assert.Equal(100u, (pixels[0] >> 16) & 0xFF);
    }

    [Fact]
    public void TheCornersAreRoundedLikeTheWindow()
    {
        var image = new BackdropImage(new Rectangle(0, 0, 20, 20), Fill(20, 20, 0xFFFFFFFF));
        var pixels = new uint[400];

        XrayPainter.Paint(pixels, new Rectangle(0, 0, 20, 20), [image], new XrayBackdrop(BlurTuning.Documented, 8, 2f, 255));

        Assert.Equal(0u, pixels[0] >> 24);
        Assert.Equal(0u, pixels[19] >> 24);
        Assert.Equal(0u, pixels[380] >> 24);
        Assert.Equal(0u, pixels[399] >> 24);
        Assert.Equal(0xFFFFFFFFu, pixels[(10 * 20) + 10]);
        Assert.Equal(0xFFFFFFFFu, pixels[10]);
        Assert.Equal(0xFFFFFFFFu, pixels[10 * 20]);
    }

    [Fact]
    public void XrayConfigIsReadWithoutWarnings()
    {
        var document = ConfigParser.Parse("""
            decoration {
                blur {
                    xray = true
                    ignore_opacity = false
                    size = 12
                    passes = 3
                    noise = 0.5
                    contrast = 1.5
                    brightness = 0.7
                    vibrancy = 0.4
                    vibrancy_darkness = 0.2
                }
            }
            """);

        var options = DecorationOptions.From(document);

        Assert.Empty(document.Warnings);
        Assert.True(options.Xray);
        Assert.False(options.IgnoreOpacity);
        Assert.Equal(new BlurTuning { Size = 12, Passes = 3, Noise = 0.5f, Contrast = 1.5f, Brightness = 0.7f, Vibrancy = 0.4f, VibrancyDarkness = 0.2f }, options.BlurTuning);
    }

    [Fact]
    public void BlurOptionsAreClampedToTheReleasesRanges()
    {
        var options = DecorationOptions.From(ConfigParser.Parse("""
            decoration:blur:size = 500
            decoration:blur:passes = 99
            decoration:blur:noise = -1
            decoration:blur:contrast = 9
            decoration:blur:brightness = 9
            decoration:blur:vibrancy = 3
            decoration:blur:vibrancy_darkness = 3
            """));

        Assert.Equal(new BlurTuning { Size = 100, Passes = 10, Noise = 0, Contrast = 2, Brightness = 2, Vibrancy = 1, VibrancyDarkness = 1 }, options.BlurTuning);
        Assert.False(DecorationOptions.Documented.Xray);
        Assert.True(DecorationOptions.Documented.IgnoreOpacity);
    }

    [Fact]
    public void ATranslucentClientGetsTheBlurredWallpaperInsteadOfTheSystemBackdrop()
    {
        var core = Desk("decoration:blur:xray = true\ndecoration:active_opacity = 0.8\ndecoration:rounding = 10\ndecoration:blur:size = 5\n");

        var decoration = For(core.Handle(Appeared(1)), 1);

        Assert.False(decoration.Blur);
        var xray = Assert.IsType<XrayBackdrop>(decoration.Xray);
        Assert.Equal(255, xray.Alpha);
        Assert.Equal(10, xray.Rounding);
        Assert.Equal(2f, xray.RoundingPower);
        Assert.Equal(5, xray.Tuning.Size);
    }

    [Fact]
    public void WithoutIgnoreOpacityTheBackdropTakesTheWindowsOpacity()
    {
        var core = Desk("decoration:blur:xray = true\ndecoration:blur:ignore_opacity = false\ndecoration:active_opacity = 0.8\n");

        var decoration = For(core.Handle(Appeared(1)), 1);

        Assert.Equal(decoration.Alpha, Assert.IsType<XrayBackdrop>(decoration.Xray).Alpha);
        Assert.Equal(204, decoration.Alpha);
    }

    [Fact]
    public void AnOpaqueClientKeepsTheSystemBackdrop()
    {
        var core = Desk("decoration:blur:xray = true\n");

        var decoration = For(core.Handle(Appeared(1)), 1);

        Assert.True(decoration.Blur);
        Assert.Null(decoration.Xray);
        Assert.Null(decoration.Behind);
    }

    [Fact]
    public void WithoutXrayATiledClientStillGetsTheSharedWallpaperBlur()
    {
        var plain = For(Desk("decoration:active_opacity = 0.8\n").Handle(Appeared(1)), 1);
        var off = For(Desk("decoration:blur:xray = true\ndecoration:blur:enabled = false\ndecoration:active_opacity = 0.8\n").Handle(Appeared(1)), 1);

        Assert.NotNull(plain.Xray);
        Assert.Null(plain.Behind);
        Assert.False(plain.Blur);
        Assert.Null(off.Xray);
        Assert.Null(off.Behind);
        Assert.False(off.Blur);
    }

    [Fact]
    public void AFullyClearClientHasNoBackdropLayer()
    {
        var clear = For(Desk("decoration:blur:xray = true\ndecoration:active_opacity = 0\n").Handle(Appeared(1)), 1);

        Assert.Null(clear.Xray);
        Assert.Null(clear.Behind);
        Assert.True(clear.Blur);
    }

    private static Core Desk(string config)
    {
        var core = new Core();
        core.Handle(new Configure(ConfigParser.Parse(config)));
        return core;
    }

    private static WindowAppeared Appeared(long window) => new(new WindowId(window), Area);

    private static Decoration For(Outcome outcome, long window)
    {
        var client = Assert.Single(outcome.State.Clients, candidate => candidate.Window == new WindowId(window));
        return Assert.Single(outcome.Decorations, decoration => decoration.Client == client.Id);
    }

    private static uint[] Fill(int width, int height, uint color)
    {
        var pixels = new uint[width * height];
        Array.Fill(pixels, color);
        return pixels;
    }

    private static uint Red(uint pixel) => (pixel >> 16) & 0xFF;

    private static uint Blue(uint pixel) => pixel & 0xFF;
}

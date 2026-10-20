using Windows.Win32.UI.WindowsAndMessaging;

namespace Winland.Windows.Tests;

public class CursorShapeTests
{
    [Theory]
    [InlineData("grabbing", (int)SYSTEM_CURSOR_ID.OCR_SIZEALL)]
    [InlineData("top_side", (int)SYSTEM_CURSOR_ID.OCR_SIZENS)]
    [InlineData("bottom_side", (int)SYSTEM_CURSOR_ID.OCR_SIZENS)]
    [InlineData("left_side", (int)SYSTEM_CURSOR_ID.OCR_SIZEWE)]
    [InlineData("right_side", (int)SYSTEM_CURSOR_ID.OCR_SIZEWE)]
    [InlineData("top_left_corner", (int)SYSTEM_CURSOR_ID.OCR_SIZENWSE)]
    [InlineData("bottom_right_corner", (int)SYSTEM_CURSOR_ID.OCR_SIZENWSE)]
    [InlineData("top_right_corner", (int)SYSTEM_CURSOR_ID.OCR_SIZENESW)]
    [InlineData("bottom_left_corner", (int)SYSTEM_CURSOR_ID.OCR_SIZENESW)]
    public void EachNameTheCoreReportsHasAWindowsCursor(string name, int cursor) =>
        Assert.Equal((SYSTEM_CURSOR_ID)cursor, CursorShape.For(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pirate")]
    public void NoNameOrAnUnknownOneIsTheUsersOwnCursor(string? name) =>
        Assert.Null(CursorShape.For(name));
}

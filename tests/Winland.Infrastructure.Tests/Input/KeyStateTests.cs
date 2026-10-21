
namespace Winland.Infrastructure.Tests;

public class KeyStateTests
{
    [Theory]
    [InlineData("Super_L", 0)]
    [InlineData("a", 0)]
    [InlineData("Return", 0)]
    [InlineData("", 38)]
    public void AKeyIsUpWhileNoHookHasSeenIt(string key, int code) => Assert.False(KeyState.IsDown(key, code));

    [Theory]
    [InlineData("nosuch", 0)]
    [InlineData("", 0)]
    [InlineData("mouse:272", 0)]
    public void WhatNamesNoKeyIsNull(string key, int code) => Assert.Null(KeyState.IsDown(key, code));
}

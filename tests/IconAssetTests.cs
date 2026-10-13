using System;
using System.Drawing;
using System.IO;
using Xunit;

namespace Flow.Launcher.Plugin.AntigravitySwitcher.Tests;

public sealed class IconAssetTests
{
    private static readonly string ImagesDir = FindImagesDirectory();

    private static string FindImagesDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Images");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "AntigravitySwitcher.sln")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Images"));
    }

    [Theory]
    [InlineData("app.png")]
    [InlineData("active.png")]
    [InlineData("standby.png")]
    [InlineData("exhausted.png")]
    [InlineData("next.png")]
    [InlineData("refresh.png")]
    [InlineData("add.png")]
    [InlineData("remove.png")]
    public void Icon_Exists_AndHas128Resolution(string iconFileName)
    {
        var path = Path.Combine(ImagesDir, iconFileName);
        Assert.True(File.Exists(path), $"Icon file {iconFileName} should exist at {path}");

        using var img = Image.FromFile(path);
        Assert.Equal(128, img.Width);
        Assert.Equal(128, img.Height);
        Assert.Equal(System.Drawing.Imaging.PixelFormat.Format32bppArgb, img.PixelFormat);
    }
}

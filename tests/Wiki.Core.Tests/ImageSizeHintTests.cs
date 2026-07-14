// SPDX-License-Identifier: GPL-3.0-or-later
using Wiki.Core.Embedding;

namespace Wiki.Core.Tests;

public class ImageSizeHintTests
{
    [Theory]
    [InlineData("300", 300.0, null)]                 // ![[img.png|300]]
    [InlineData(" 300 ", 300.0, null)]
    [InlineData("300x200", 300.0, 200.0)]            // ![[img.png|300x200]]
    [InlineData("300X200", 300.0, 200.0)]
    [InlineData("photo|300", 300.0, null)]           // ![photo|300](img.png) — alt carries the hint after the last pipe
    [InlineData("a|b|640x480", 640.0, 480.0)]
    public void Parses_pipe_size_hints(string alias, double? w, double? h)
    {
        var (width, height) = ImageSizeHint.Parse(alias);
        Assert.Equal(w, width);
        Assert.Equal(h, height);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("left")]                            // a plain alias is not a size
    [InlineData("photo")]
    [InlineData("0")]                               // zero/negative/huge are not sane sizes
    [InlineData("-40")]
    [InlineData("999999")]
    [InlineData("300x")]                            // malformed pair
    [InlineData("x200")]
    [InlineData("1e3")]                             // integers only
    public void Non_sizes_yield_no_hint(string? alias)
    {
        Assert.Equal((null, null), ImageSizeHint.Parse(alias));
    }
}

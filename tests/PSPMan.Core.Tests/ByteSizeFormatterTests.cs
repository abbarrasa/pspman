using System.Globalization;
using PSPMan.Core.Games;

namespace PSPMan.Core.Tests;

public class ByteSizeFormatterTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KiB")]
    [InlineData(1536L, "1.5 KiB")]
    [InlineData(1_572_864L, "1.5 MiB")]
    [InlineData(1_073_741_824L, "1 GiB")]
    [InlineData(1_099_511_627_776L, "1 TiB")]
    [InlineData(1_125_899_906_842_624L, "1 PiB")]
    [InlineData(1_152_921_504_606_846_976L, "1 EiB")]
    public void FormatsBytesWithBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }

    [Fact]
    public void UsesInvariantDecimalSeparatorRegardlessOfCurrentCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");

            Assert.Equal("1.5 MiB", ByteSizeFormatter.Format(1_572_864));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}

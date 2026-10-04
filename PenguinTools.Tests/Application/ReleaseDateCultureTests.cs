using System.Globalization;
using PenguinTools.Application;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using Xunit;

namespace PenguinTools.Tests.Application;

public sealed class ReleaseDateCultureTests
{
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void SerializedReleaseDatesUseGregorianCalendar(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var meta = new Meta
            {
                Id = 4321,
                Difficulty = Difficulty.Master,
                ReleaseDate = new DateTime(2026, 10, 4)
            };

            var music = new MusicXml(new Dictionary<Difficulty, Meta> { [meta.Difficulty] = meta }, meta.Difficulty);
            var metadata = ChartMetadata.CreateChartConversionMetadata(meta);

            Assert.Equal("20261004", music.ReleaseDate);
            Assert.Equal("2026-10-04", metadata.ReleaseDate);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}

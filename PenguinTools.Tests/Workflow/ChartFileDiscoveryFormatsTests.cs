using System.Text.Json;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Xml;
using PenguinTools.Workflow;
using Xunit;

namespace PenguinTools.Tests.Workflow;

public class ChartFileDiscoveryFormatsTests
{
    [Fact]
    public void TryParse_AcceptsBracketedOrderedList()
    {
        var ok = ChartFileDiscoveryFormats.TryParse("[ugc, sus, mgxc]", out var formats, out var error);

        Assert.True(ok, error?.Key);
        Assert.Equal([ChartFileFormat.Ugc, ChartFileFormat.Sus, ChartFileFormat.Mgxc], formats);
    }

    [Fact]
    public void TryParse_RemovesDuplicates_PreservingFirstOccurrence()
    {
        var ok = ChartFileDiscoveryFormats.TryParse("ugc, sus, ugc, mgxc, sus", out var formats, out var error);

        Assert.True(ok, error?.Key);
        Assert.Equal([ChartFileFormat.Ugc, ChartFileFormat.Sus, ChartFileFormat.Mgxc], formats);
    }

    [Fact]
    public void OptionDocumentJson_ReadsNewArraySyntax()
    {
        const string json = """
                            {
                              "optionName": "TEST",
                              "chartFileDiscovery": ["ugc", "sus", "mgxc"]
                            }
                            """;

        var document = JsonSerializer.Deserialize<OptionDocument>(json, OptionDocumentJson.Default);

        Assert.NotNull(document);
        Assert.Equal([ChartFileFormat.Ugc, ChartFileFormat.Sus, ChartFileFormat.Mgxc], document.ChartFileDiscovery);
    }

    [Fact]
    public void OptionDocument_GeneratesOptionIdByDefault()
    {
        var document = new OptionDocument();

        Assert.False(string.IsNullOrWhiteSpace(document.OptionId));
    }

    [Fact]
    public void OptionDocument_DefaultsReleaseTagSettings()
    {
        var document = new OptionDocument();

        Assert.Equal(ReleaseTag.CustomDefaultId, document.SelectedReleaseTagId);
        Assert.False(document.CustomReleaseTagXml);
        Assert.Equal(ReleaseTag.CustomDefaultId, document.CustomReleaseTagId);
        Assert.Equal(ReleaseTag.CustomDefaultTitleName, document.CustomReleaseTagTitleName);
        Assert.False(document.CustomGenre);
        Assert.Equal(GenreDefaults.SelectedDefaultId, document.SelectedGenreId);
        Assert.Equal(GenreDefaults.CustomDefaultId, document.CustomGenreId);
        Assert.Equal(GenreDefaults.CustomDefaultName, document.CustomGenreName);
        Assert.True(document.OverrideChartGenre);
    }

    [Fact]
    public void OptionDocumentJson_GeneratesOptionIdWhenMissing()
    {
        const string json = """
                            {
                              "optionName": "TEST"
                            }
                            """;

        var document = JsonSerializer.Deserialize<OptionDocument>(json, OptionDocumentJson.Default);

        Assert.NotNull(document);
        Assert.False(string.IsNullOrWhiteSpace(document.OptionId));
    }

    [Fact]
    public void OptionDocumentJson_RejectsLegacyNumericMode()
    {
        const string json = """
                            {
                              "optionName": "TEST",
                              "chartFileDiscovery": 2
                            }
                            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OptionDocument>(json, OptionDocumentJson.Default));
    }

    [Fact]
    public void OptionDocumentJson_RejectsLegacyEnumName()
    {
        const string json = """
                            {
                              "optionName": "TEST",
                              "chartFileDiscovery": "ugcFirst"
                            }
                            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<OptionDocument>(json, OptionDocumentJson.Default));
    }

    [Fact]
    public void OptionDocumentJson_WritesOrderedArraySyntax()
    {
        var document = new OptionDocument
        {
            OptionName = "TEST",
            ChartFileDiscovery = [ChartFileFormat.Ugc, ChartFileFormat.Sus, ChartFileFormat.Mgxc]
        };

        var json = JsonSerializer.Serialize(document, OptionDocumentJson.Default);

        Assert.Contains("\"chartFileDiscovery\": [", json);
        Assert.Contains("\"ugc\"", json);
        Assert.Contains("\"sus\"", json);
        Assert.Contains("\"mgxc\"", json);
    }

    [Fact]
    public void OptionDocumentJson_WritesOptionId()
    {
        var document = new OptionDocument
        {
            OptionName = "TEST",
            OptionId = "T001"
        };

        var json = JsonSerializer.Serialize(document, OptionDocumentJson.Default);

        Assert.Contains("\"optionId\": \"T001\"", json);
    }

    [Fact]
    public void OptionDocumentJson_WritesReleaseTagSettings()
    {
        var document = new OptionDocument
        {
            OptionName = "TEST",
            SelectedReleaseTagId = 12,
            CustomReleaseTagXml = true,
            CustomReleaseTagId = 123,
            CustomReleaseTagTitleName = "My Pack",
            CustomGenre = true,
            SelectedGenreId = 5,
            CustomGenreId = 1000,
            CustomGenreName = "Custom Genre",
            OverrideChartGenre = false
        };

        var json = JsonSerializer.Serialize(document, OptionDocumentJson.Default);

        Assert.Contains("\"selectedReleaseTagId\": 12", json);
        Assert.Contains("\"customReleaseTagXml\": true", json);
        Assert.Contains("\"customReleaseTagId\": 123", json);
        Assert.Contains("\"customReleaseTagTitleName\": \"My Pack\"", json);
        Assert.Contains("\"customGenre\": true", json);
        Assert.Contains("\"selectedGenreId\": 5", json);
        Assert.Contains("\"customGenreId\": 1000", json);
        Assert.Contains("\"customGenreName\": \"Custom Genre\"", json);
        Assert.Contains("\"overrideChartGenre\": false", json);
    }
}
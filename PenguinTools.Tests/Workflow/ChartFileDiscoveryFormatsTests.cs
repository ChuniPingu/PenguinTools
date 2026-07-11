using System.Text.Json;
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

        Assert.Equal(ReleaseTag.DefaultId, document.ReleaseTagId);
        Assert.Equal(ReleaseTag.DefaultTitleName, document.ReleaseTagTitleName);
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
            ReleaseTagId = 123,
            ReleaseTagTitleName = "My Pack"
        };

        var json = JsonSerializer.Serialize(document, OptionDocumentJson.Default);

        Assert.Contains("\"releaseTagId\": 123", json);
        Assert.Contains("\"releaseTagTitleName\": \"My Pack\"", json);
    }
}
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using PenguinTools.Media;

namespace PenguinTools.Workflow;

internal sealed record OptionCachedConversion(
    string Key,
    OptionConversionCacheState? State,
    IReadOnlyList<OptionConversionArtifact> Outputs);

internal static class OptionConversionCacheArtifacts
{
    public static Task<OptionCachedConversion> CreateStageAsync(
        OptionBookSnapshot book,
        string stageOutputFolder,
        CancellationToken ct)
    {
        if (book.StageId is not { } stageId)
            throw new ArgumentException("Stage id is required for stage cache artifacts.", nameof(book));

        var stageXml = new StageXml(stageId, book.NotesFieldLine);
        var stageFolder = Path.Combine(stageOutputFolder, stageXml.DataName);
        var outputs = new[]
        {
            new OptionConversionArtifact("stageXml", Path.Combine(stageFolder, "Stage.xml")),
            new OptionConversionArtifact("baseAfb", Path.Combine(stageFolder, stageXml.BaseFile)),
            new OptionConversionArtifact("notesFieldAfb", Path.Combine(stageFolder, stageXml.NotesFieldFile))
        };

        return CreateAsync(
            $"stage:{stageId}",
            CreateStageRecipe(book),
            new Dictionary<string, string>
            {
                ["background"] = book.BookMeta.FullBgiFilePath
            },
            outputs,
            ct);
    }

    public static Task<OptionCachedConversion> CreateJacketAsync(
        string musicDataName,
        string jacketPath,
        string outputPath,
        CancellationToken ct)
    {
        return CreateAsync(
            $"jacket:{musicDataName}",
            new Dictionary<string, string?>
            {
                ["kind"] = "jacket",
                ["recipeVersion"] = "1"
            },
            new Dictionary<string, string>
            {
                ["jacket"] = jacketPath
            },
            [new OptionConversionArtifact("jacketDds", outputPath)],
            ct);
    }

    public static Task<OptionCachedConversion> CreateAudioAsync(
        Meta meta,
        string cueFileFolder,
        CancellationToken ct)
    {
        return CreateAudioAsync(
            meta,
            cueFileFolder,
            AudioConvertRequest.DefaultHcaEncryptionKey,
            ct);
    }

    public static Task<OptionCachedConversion> CreateAudioAsync(
        Meta meta,
        string cueFileFolder,
        ulong hcaEncryptionKey,
        CancellationToken ct)
    {
        if (meta.Id is not { } songId)
            throw new ArgumentException("Song id is required for audio cache artifacts.", nameof(meta));

        var cueXml = new CueFileXml(songId);
        var outputDir = Path.Combine(cueFileFolder, cueXml.DataName);
        var outputs = new[]
        {
            new OptionConversionArtifact("cueFileXml", Path.Combine(outputDir, "CueFile.xml")),
            new OptionConversionArtifact("acb", Path.Combine(outputDir, cueXml.AcbFile)),
            new OptionConversionArtifact("awb", Path.Combine(outputDir, cueXml.AwbFile))
        };

        return CreateAsync(
            $"audio:{songId}",
            CreateAudioRecipe(meta, hcaEncryptionKey),
            new Dictionary<string, string>
            {
                ["bgm"] = meta.FullBgmFilePath
            },
            outputs,
            ct);
    }

    private static async Task<OptionCachedConversion> CreateAsync(
        string key,
        IReadOnlyDictionary<string, string?> recipe,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyList<OptionConversionArtifact> outputs,
        CancellationToken ct)
    {
        var state = await OptionConversionCacheValidator.CreateStateAsync(recipe, inputs, ct);
        return new OptionCachedConversion(key, state, outputs);
    }

    private static Dictionary<string, string?> CreateStageRecipe(OptionBookSnapshot book)
    {
        return new Dictionary<string, string?>
        {
            ["kind"] = "stage",
            ["recipeVersion"] = "2",
            ["stageId"] = book.StageId?.ToString(),
            ["notesFieldLine.id"] = book.NotesFieldLine.Id.ToString(),
            ["notesFieldLine.str"] = book.NotesFieldLine.Str,
            ["notesFieldLine.data"] = book.NotesFieldLine.Data,
            ["effect.count"] = "0"
        };
    }

    private static Dictionary<string, string?> CreateAudioRecipe(Meta meta, ulong hcaEncryptionKey)
    {
        return new Dictionary<string, string?>
        {
            ["kind"] = "audio",
            ["recipeVersion"] = "2",
            ["songId"] = meta.Id?.ToString(),
            ["bgmRealOffset"] = OptionConversionCacheValidator.FormatInvariant(meta.BgmRealOffset),
            ["bgmPreviewStart"] = OptionConversionCacheValidator.FormatInvariant(meta.BgmPreviewStart),
            ["bgmPreviewStop"] = OptionConversionCacheValidator.FormatInvariant(meta.BgmPreviewStop),
            ["bgmEnableBarOffset"] = meta.BgmEnableBarOffset.ToString(),
            ["bgmBarOffset"] = OptionConversionCacheValidator.FormatInvariant(meta.BgmBarOffset),
            ["hcaEncryptionKey"] = hcaEncryptionKey.ToString()
        };
    }
}
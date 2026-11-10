using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.Metadata;
using PenguinTools.Core.Xml;
using Xunit;

namespace PenguinTools.Tests;

public sealed class ResourceIdTests
{
    [Fact]
    public void AutomaticIds_HandleUpperBoundAndSongIdChangesWithoutWrapping()
    {
        var meta = new Meta { Id = ResourceIds.MaxSongId };
        Assert.Equal(2_001_000_000, meta.StageId);
        Assert.Equal(2_001_000_000, meta.UnlockEventId);
        Assert.Equal(2_001_000_000, meta.EffectiveWallId);

        meta.Id++;
        Assert.Null(meta.StageId);
        Assert.Null(meta.UnlockEventId);
        Assert.Null(meta.EffectiveWallId);

        meta.Id = 0;
        Assert.Equal(CustomAssetIds.SongOffset, meta.StageId);
        Assert.Equal(CustomAssetIds.SongOffset, meta.UnlockEventId);
        Assert.Equal(CustomAssetIds.SongOffset, meta.EffectiveWallId);

        meta.StageId = 0;
        meta.UnlockEventId = 0;
        meta.WallId = 0;
        meta.Id = int.MaxValue;
        Assert.Equal(0, meta.StageId);
        Assert.Equal(0, meta.UnlockEventId);
        Assert.Equal(0, meta.EffectiveWallId);
    }

    [Fact]
    public void XmlResources_PreserveSignedDefinitionsAndUnspecifiedReferences()
    {
        Assert.Throws<DiagnosticException>(() => new EventXml(-1, EventXml.MusicType.Ultima, []));
        XmlElement[] resources = [new StageXml(-2, Entry.Default), new CueFileXml(-2), new ReleaseTag(-2)];
        foreach (var resource in resources)
        {
            Assert.Equal("-2", resource.ToDocument().Root!.Element("name")!.Element("id")!.Value);
        }
        var assets = TestAssets.Load();
        assets.DefineEntry(AssetType.GenreNames, new(-2, "Custom genre"));
        Assert.Contains(assets.GenreNames, entry => entry.Id == -2);
        var xml = new StageXml(0, Entry.Default).ToDocument().Root!;
        Assert.Equal("-1", xml.Element("notesFieldLine")!.Element("id")!.Value);
        var meta = new Meta { Id = 0, Genre = new Entry(-1, "Editor genre"), WeTag = new Entry(-1, "Editor tag") };
        var music = new MusicXml(new() { [Difficulty.Master] = meta }, Difficulty.Master).ToDocument().Root!;
        Assert.Equal("Editor genre", music.Element("genreNames")!.Element("list")!.Element("StringID")!.Element("str")!.Value);
        Assert.Equal("Editor tag", music.Element("worldsEndTagName")!.Element("str")!.Value);
    }
}

using PenguinTools.Chart.Converter.c2s;
using PenguinTools.Chart.Converter.ugc;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.c2s;
using PenguinTools.Chart.Writer.c2s;
using Xunit;
using C = PenguinTools.Chart.Models.c2s;
using U = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Tests.Parser;

public sealed class C2sPositivePathSchedulingTests
{
    [Theory]
    [InlineData(5, "ground-first")]
    [InlineData(5, "air-first")]
    [InlineData(5, "paired")]
    [InlineData(5, "reverse-paired")]
    [InlineData(5, "rotated")]
    [InlineData(5, "crossed")]
    [InlineData(240, "ground-first")]
    [InlineData(240, "air-first")]
    [InlineData(240, "paired")]
    [InlineData(240, "reverse-paired")]
    [InlineData(240, "rotated")]
    [InlineData(240, "crossed")]
    public async Task PositiveOverlappingPaths_KeepTheirGeometryAndAirOwnership(int unit, string declarationOrder)
    {
        var source = CreateChart(unit, declarationOrder);

        var converted = new C2SChartConverter(new C2SConvertRequest(source)).Convert();

        Assert.True(converted.Succeeded, converted.ToString());
        Assert.Empty(converted.Diagnostics.Diagnostics);
        var notes = converted.Value!.Notes;
        var positions = notes.Select((note, index) => (note, index)).ToDictionary(p => p.note, p => p.index);
        foreach (var note in notes.OfType<C.IPairable>())
        {
            Assert.NotNull(note.Parent);
            Assert.True(positions.TryGetValue(note.Parent, out var parentIndex), "The converted AIR parent must be an emitted record.");
            Assert.True(parentIndex < positions[(C.Note)note], "Convert() must return the parent before every AIR head or continuation.");
        }
        Assert.All(notes.OfType<C.LongNote>(), note =>
            Assert.True(note.EndTick.Round - note.Tick.Round >= 5, "Every test segment must retain at least one C2S tick."));

        var path = TestTempPaths.Create(".c2s");
        try
        {
            var written = await new C2SChartWriter(new C2SWriteRequest(path, converted.Value))
                .WriteAsync(TestContext.Current.CancellationToken);
            Assert.True(written.Succeeded, written.ToString());
            Assert.Empty(written.Diagnostics.Diagnostics);
            var parsed = await new C2SParser(new C2SParseRequest(path)).ParseAsync(TestContext.Current.CancellationToken);
            Assert.True(parsed.Succeeded, parsed.ToString());
            Assert.Empty(parsed.Diagnostics.Diagnostics);
            var restored = new UgcChartConverter(new UgcConvertRequest(parsed.Value!)).Convert();
            Assert.True(restored.Succeeded, restored.ToString());
            Assert.Empty(restored.Diagnostics.Diagnostics);

            AssertChartPaths(source, restored.Value!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static U.Chart CreateChart(int unit, string declarationOrder)
    {
        var chart = new U.Chart();
        chart.Extras.FixedZeroOrigin = true;
        chart.Meta.ExVersion = 1;
        chart.Meta.ExLongEnabled = true;
        chart.Events.AppendChild(new U.BpmEvent { Bpm = 120 });
        chart.Events.AppendChild(new U.BeatEvent());
        var ground = Enumerable.Range(0, 6).Select(i =>
        {
            var slide = new U.Slide
            {
                Tick = 0,
                Lane = i * 2,
                Width = 2 + i % 3 * 2,
                NoLine = i % 2 != 0,
                Effect = i switch { 2 => ExEffect.UP, 3 => ExEffect.DW, 5 => ExEffect.CE, _ => null }
            };
            slide.AppendChild(new U.SlideJoint
            {
                Tick = unit,
                Lane = i,
                Width = 4,
                Joint = Joint.C,
                NoLine = slide.NoLine
            });
            slide.AppendChild(new U.SlideJoint
            {
                Tick = 4 * unit,
                Lane = 6,
                Width = 4,
                Joint = Joint.D,
                NoLine = slide.NoLine
            });
            return slide;
        }).ToList();
        // This is an independent head at the common endpoint, not a continuation
        // of any of the six overlapping paths.
        var touching = new U.Slide { Tick = 4 * unit, Lane = 6, Width = 4, Effect = ExEffect.RC };
        touching.AppendChild(new U.SlideJoint { Tick = 5 * unit, Lane = 10, Width = 2, Joint = Joint.C });
        touching.AppendChild(new U.SlideJoint { Tick = 9 * unit, Lane = 12, Width = 4, Joint = Joint.D });
        ground.Add(touching);

        var air = new U.Air { Direction = AirDirection.UR, Color = Color.PNK };
        var shortHold = Hold(unit, 6);
        var longHold = Hold(unit, 5, 6, 8);
        var shortSlide = AirSlide(unit, (6, 8, 2, 60m));
        var longSlide = AirSlide(unit, (5, 4, 6, 40m), (6, 8, 2, 60m), (8, 12, 4, 100m));
        U.NegativeNote[] actions = [air, shortHold, longHold, shortSlide, longSlide];
        for (var i = 0; i < actions.Length; i++)
        {
            Assert.IsType<U.SlideJoint>(ground[i + 1].LastChild).MakePair(actions[i]);
        }

        IEnumerable<U.Note> Paired(IEnumerable<U.Slide> slides, bool airFirst) => slides.SelectMany(slide =>
            Assert.IsType<U.SlideJoint>(slide.LastChild).PairNote is { } action
                ? airFirst ? new U.Note[] { action, slide } : [slide, action]
                : [slide]);
        IEnumerable<U.Note> ordered = declarationOrder switch
        {
            "ground-first" => ground.Cast<U.Note>().Concat(actions),
            "air-first" => actions.Reverse().Cast<U.Note>().Concat(ground.AsEnumerable().Reverse()),
            "paired" => Paired(ground, false),
            "reverse-paired" => Paired(ground.AsEnumerable().Reverse(), true),
            "rotated" => new[] { ground[3], ground[0], ground[5], ground[1], ground[6], ground[4], ground[2] }
                .Cast<U.Note>().Concat([actions[3], actions[0], actions[4], actions[1], actions[2]]),
            "crossed" => [actions[2], ground[4], actions[1], ground[0], actions[4], ground[6],
                ground[3], actions[0], ground[5], ground[1], actions[3], ground[2]],
            _ => throw new ArgumentOutOfRangeException(nameof(declarationOrder))
        };
        foreach (var note in ordered)
        {
            chart.Notes.AppendChild(note);
        }
        return chart;
    }

    private static U.AirHold Hold(int unit, params int[] ticks)
    {
        var hold = new U.AirHold { Color = Color.PNK };
        foreach (var tick in ticks)
        {
            hold.AppendChild(new U.AirHoldJoint { Tick = tick * unit, Joint = Joint.D });
        }
        return hold;
    }

    private static U.AirSlide AirSlide(int unit, params (int Tick, int Lane, int Width, decimal Height)[] points)
    {
        var slide = new U.AirSlide { Height = 20m, Color = Color.PNK };
        foreach (var point in points)
        {
            slide.AppendChild(new U.AirSlideJoint
            {
                Tick = point.Tick * unit,
                Lane = point.Lane,
                Width = point.Width,
                Height = point.Height,
                Joint = Joint.D
            });
        }
        return slide;
    }

    private static void AssertChartPaths(U.Chart expected, U.Chart actual)
    {
        var expectedSlides = expected.Notes.Children.OfType<U.Slide>().ToArray();
        var actualSlides = actual.Notes.Children.OfType<U.Slide>().ToDictionary(s => (s.Tick.Original, s.Lane, s.Width));
        Assert.Equal(expectedSlides.Length, actualSlides.Count);
        Assert.Equal(expected.Notes.Children.OfType<U.NegativeNote>().Count(), actual.Notes.Children.OfType<U.NegativeNote>().Count());
        foreach (var source in expectedSlides)
        {
            var restored = actualSlides[(source.Tick.Original, source.Lane, source.Width)];
            Assert.Equal(source.Effect, restored.Effect);
            Assert.Equal(source.NoLine, restored.NoLine);
            static IEnumerable<(int, int, int, Joint)> GroundPoints(U.Slide slide) => slide.Children
                .Cast<U.SlideJoint>().Select(n => (n.Tick.Original, n.Lane, n.Width, n.Joint));
            Assert.Equal(GroundPoints(source), GroundPoints(restored));
            // NoLine describes the following segment, which a terminal joint does not have.
            Assert.Equal(source.Children.Cast<U.SlideJoint>().SkipLast(1).Select(n => n.NoLine),
                restored.Children.Cast<U.SlideJoint>().SkipLast(1).Select(n => n.NoLine));
            var expectedAir = Assert.IsType<U.SlideJoint>(source.LastChild).PairNote;
            var actualEnd = Assert.IsType<U.SlideJoint>(restored.LastChild);
            if (expectedAir is null)
            {
                Assert.Null(actualEnd.PairNote);
                continue;
            }
            var actualAir = Assert.IsAssignableFrom<U.NegativeNote>(actualEnd.PairNote);
            Assert.Equal(expectedAir.GetType(), actualAir.GetType());
            Assert.Same(actualEnd, actualAir.PairNote);
            Assert.Equal(expectedAir.Tick, actualAir.Tick);
            switch (expectedAir)
            {
                case U.Air arrow:
                    var actualArrow = Assert.IsType<U.Air>(actualAir);
                    Assert.Equal(arrow.Direction, actualArrow.Direction);
                    Assert.Equal(arrow.Color, actualArrow.Color);
                    break;
                case U.AirHold hold:
                    var actualHold = Assert.IsType<U.AirHold>(actualAir);
                    Assert.Equal(hold.Color, actualHold.Color);
                    Assert.Equal(hold.Children.Cast<U.AirHoldJoint>().Select(n => (n.Tick.Original, n.Joint)),
                        actualHold.Children.Cast<U.AirHoldJoint>().Select(n => (n.Tick.Original, n.Joint)));
                    break;
                case U.AirSlide slide:
                    var actualSlide = Assert.IsType<U.AirSlide>(actualAir);
                    Assert.Equal(slide.Color, actualSlide.Color);
                    Assert.Equal(slide.Height, actualSlide.Height);
                    static IEnumerable<(int, int, int, decimal, Joint)> AirPoints(U.AirSlide air) => air.Children
                        .Cast<U.AirSlideJoint>().Select(n => (n.Tick.Original, n.Lane, n.Width, n.Height, n.Joint));
                    Assert.Equal(AirPoints(slide), AirPoints(actualSlide));
                    break;
            }
        }
    }
}

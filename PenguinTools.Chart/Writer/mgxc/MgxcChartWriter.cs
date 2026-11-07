using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PenguinTools.Chart.Models;
using PenguinTools.Chart.Parser.mgxc;
using PenguinTools.Core;
using PenguinTools.Core.Diagnostic;
using PenguinTools.Core.IO;
using PenguinTools.Core.Metadata;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Writer.mgxc;
/// <summary>Writes Margrete binary MGXC for the requested editor capabilities.</summary>
public sealed class MgxcChartWriter(MgxcWriteRequest request)
{
    private int Version => EditorTargetRules.Version(request.Target, ugc: false);
    private const int DefaultHeight = 80;
    private const sbyte NoLineVariation = 35;
    private const string DefaultBookmarkColor = "FFFFFF";

    private UmgrModel.Chart _chart = null!;
    private bool _timingBookmarksRequired;
    private readonly List<ExTapRoleSnapshot> _writtenExTapRoles = [];
    private readonly List<UmgrModel.Slide> _writtenSlides = [];
    private readonly List<UmgrModel.Note> _writtenCurvePaths = [];
    private byte[] _writtenNotePayload = [];

    private readonly record struct EffectCarrierKey(
        int Tick,
        int Lane,
        int Width);

    private readonly record struct EffectCarrierPlan(
        ExEffect Effect,
        int Timeline,
        UmgrModel.ExTapRole Role);

    public async Task<OperationResult> WriteAsync(CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
        ArgumentNullException.ThrowIfNull(request.Chart);
        var diagnostics = new DiagnosticCollector();
        try
        {
            _chart = EditorProjection.Create(request.Chart, ugc: false, diagnostics, request.Target);
        }
        catch (DiagnosticException ex)
        {
            diagnostics.Report(ex.ToDiagnostic().WithPathFallback(request.Path));
            return OperationResult.Failure().WithDiagnostics(diagnostics);
        }
        CurveExpansion.Validate(_chart);
        _chart.Extras.FixedZeroOrigin |= _chart.Extras.UsesFixedCoordinateOrigin;
        _timingBookmarksRequired = (_chart.Extras.FixedZeroOrigin || _chart.Extras.PrerollBar1Applied) &&
            Encoding.UTF8.GetByteCount(_chart.Extras.CopyrightWithTiming(_chart.Meta.Copyright)) > ushort.MaxValue;
        _chart.Extras.UgcNoteSnapshots.Clear();
        _chart.Extras.UgcSnapshotVersion = 0;
        _chart.Extras.UgcContextKey = null;
        _chart.Extras.UgcMusicalKey = null;
        _chart.Extras.UgcSlaKey = null;
        try
        {
            await AtomicFile.WriteAsync(request.Path, async (stream, token) =>
            {
                await using var buffer = new MemoryStream();
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
                {
                    WriteFile(writer);
                    writer.Flush();
                }

                buffer.Position = 0;
                await buffer.CopyToAsync(stream, token);
                await stream.FlushAsync(token);
            }, ct);
        }
        catch (DiagnosticException ex)
        {
            diagnostics.Report(ex.ToDiagnostic().WithPathFallback(request.Path));
            return OperationResult.Failure().WithDiagnostics(diagnostics);
        }
        return OperationResult.Success().WithDiagnostics(diagnostics);
    }

    private void WriteFile(BinaryWriter bw)
    {
        _chart.Extras.CaptureSlideEffects(_chart);
        _chart.Extras.RoundTripBookmarks = C2sRoundTripComment.FormatBookmarks(_chart.Meta).ToList();
        using (var payload = new MemoryStream())
        {
            using var contentWriter = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true);
            WriteEvents(contentWriter, includeExtras: false);
            var noteOffset = checked((int)payload.Position);
            WriteNotes(contentWriter);
            contentWriter.Flush();
            _writtenNotePayload = payload.ToArray()[noteOffset..];
            _chart.Extras.MgxcNoteSnapshots = MgxcNoteRoles.Capture(
                payload.ToArray().AsSpan(noteOffset), _writtenExTapRoles, Version);
            _chart.Extras.BinaryContentKey = Convert.ToHexString(SHA256.HashData(payload.ToArray()));
        }
        bw.Write(Encoding.ASCII.GetBytes("MGXC"));
        var sizePos = bw.BaseStream.Position;
        bw.Write(0);
        bw.Write(Version);

        WriteBlock(bw, "meta", WriteMeta);
        WriteBlock(bw, "evnt", writer => WriteEvents(writer));
        WriteBlock(bw, "dat2", WriteNotes);

        var end = bw.BaseStream.Position;
        bw.BaseStream.Position = sizePos;
        bw.Write(checked((int)(end - 8)));
        bw.BaseStream.Position = end;
    }

    private static void WriteBlock(BinaryWriter bw, string name, Action<BinaryWriter> write)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        var sizePos = bw.BaseStream.Position;
        bw.Write(0);
        var start = bw.BaseStream.Position;
        write(bw);
        var end = bw.BaseStream.Position;
        bw.BaseStream.Position = sizePos;
        bw.Write(checked((int)(end - start)));
        bw.BaseStream.Position = end;
    }

    private void WriteMeta(BinaryWriter bw)
    {
        var m = _chart.Meta;
        WriteStringField(bw, "titl", m.Title);
        WriteStringField(bw, "sort", m.SortName);
        WriteStringField(bw, "arts", m.Artist);
        WriteStringField(bw, "genr", m.Genre?.Str ?? "");
        WriteStringField(bw, "dsgn", m.Designer);
        WriteIntField(bw, "diff", DifficultyValue(m.Difficulty));
        WriteStringField(bw, "plvl", m.Difficulty == Difficulty.WorldsEnd
            ? WeLevel(m.WeDifficulty)
            : FormatPlayLevel(m.Level));
        WriteStringField(bw, "weat", m.WeTag?.Str ?? "");
        WriteDoubleField(bw, "cnst", m.Difficulty == Difficulty.WorldsEnd ? 0 : m.Level);
        WriteStringField(bw, "sgid", string.IsNullOrWhiteSpace(m.MgxcId)
            ? m.Id?.ToString(CultureInfo.InvariantCulture) ?? ""
            : m.MgxcId);
        WriteStringField(bw, "wvfn", ChartMediaPaths.ForOutput(m.BgmFilePath, m.FilePath, request.Path));
        WriteDoubleField(bw, "wvof", m.BgmManualOffset);
        WriteDoubleField(bw, "wvp0", m.BgmPreviewStart);
        WriteDoubleField(bw, "wvp1", m.BgmPreviewStop);
        WriteStringField(bw, "jack", ChartMediaPaths.ForOutput(m.JacketFilePath, m.FilePath, request.Path));
        WriteStringField(bw, "bgfn", m.IsCustomStage ? ChartMediaPaths.ForOutput(m.BgiFilePath, m.FilePath, request.Path) : "");
        WriteStringField(bw, "bgsc", m.BackgroundScene);
        WriteIntField(bw, "bgsy", m.BackgroundSync ? 1 : 0);
        WriteStringField(bw, "flcl", m.FieldColorName);
        WriteIntField(bw, "flcx", m.FieldColorIndex);
        WriteStringField(bw, "flbg", ChartMediaPaths.ForOutput(m.FieldImage, m.FilePath, request.Path));
        WriteStringField(bw, "flsc", m.FieldScene);
        WriteIntField(bw, "mtil", m.MainTil);
        WriteDoubleField(bw, "mbpm", m.MainBpm);
        WriteIntField(bw, "ttrl", _chart.Extras.Tutorial ? 1 : 0);
        WriteIntField(bw, "sofs", m.BgmEnableBarOffset ? 1 : 0);
        WriteIntField(bw, "uclk", _chart.Extras.ClickEnabled ? 1 : 0);
        WriteIntField(bw, "xlng", m.ExLongEnabled ? 1 : 0);
        WriteIntField(bw, "xfld", m.ExtendedField ? 1 : 0);
        WriteIntField(bw, "bgmw", m.WaitBgmEnd ? 1 : 0);
        WriteStringField(bw, "atls", m.Authors);
        WriteStringField(bw, "atst", m.AuthorSites);
        WriteStringField(bw, "durl", m.DownloadUrl);
        WriteStringField(bw, "lcpy", _timingBookmarksRequired
            ? ChartExtras.CopyrightText(m.Copyright)
            : _chart.Extras.CopyrightWithTiming(m.Copyright));
        WriteStringField(bw, "ltyp", m.License);
        WriteStringField(bw, "lurl", m.LicenseUrl);
        WriteIntField(bw, "xver", m.ExVersion);
        WriteStringField(bw, "cmmt", C2sRoundTripComment.Strip(m.Comment));
        WriteIntField(bw, "CTCK", 0);
        WriteStringField(bw, "LXFN", "");
        WriteDoubleField(bw, "HSCL", 10);
        if (Version == 4)
        {
            WriteIntField(bw, "xh32", request.Target == EditorTarget.Extended ? 1 : 0);
        }
        if (request.Target == EditorTarget.Extended)
        {
            SlideSegmentMetadata.WriteBinary(bw, _writtenSlides);
            MargreteCurveMetadata.Write(bw, _writtenCurvePaths, _chart.Extras, _writtenNotePayload);
        }
        bw.Write(0);
        bw.Write((short)0);
        bw.Write((short)0);
    }

    private void WriteEvents(BinaryWriter bw, bool includeExtras = true)
    {
        WriteBookmarks(bw, includeExtras);
        WriteTimingEvents(bw);
        foreach (var marker in _chart.Events.Children.OfType<UmgrModel.BreakingMarker>())
        {
            bw.Write("mbkm"u8);
            WriteIntFieldValue(bw, marker.Tick.Original);
            bw.Write(0);
        }
        foreach (var image in _chart.Extras.EditorImageEvents)
        {
            bw.Write(image);
        }
    }

    private void WriteBookmarks(BinaryWriter bw, bool includeExtras)
    {
        if (includeExtras)
        {
            if (_timingBookmarksRequired)
            {
                if (_chart.Extras.FixedZeroOrigin)
                {
                    WriteBookmark(bw, 0, ChartExtras.FixedZeroOriginMarker, DefaultBookmarkColor);
                }
                if (_chart.Extras.PrerollBar1Applied)
                {
                    WriteBookmark(bw, 0, ChartExtras.PrerollBar1Marker, DefaultBookmarkColor);
                }
            }
            WriteBookmark(bw, 0, _chart.Extras.ToBookmark(), DefaultBookmarkColor);
        }

        foreach (var bookmark in _chart.Events.Children.OfType<UmgrModel.BookmarkEvent>()
                     .Where(bookmark => !C2sRoundTripComment.IsRoundTripLine(bookmark.Tag))
                     .OrderBy(bookmark => bookmark.Tick.Original))
        {
            WriteBookmark(
                bw,
                bookmark.Tick.Original,
                bookmark.Tag,
                string.IsNullOrWhiteSpace(bookmark.Rgb) ? DefaultBookmarkColor : bookmark.Rgb,
                bookmark.Id);
        }
    }

    private void WriteTimingEvents(BinaryWriter bw)
    {
        var beats = _chart.Events.Children.OfType<UmgrModel.BeatEvent>()
            .Where(x => x.Numerator >= 0 && x.Denominator > 0)
            .OrderBy(x => x.Bar).ToArray();
        if (beats.Length == 0)
        {
            beats = [new UmgrModel.BeatEvent { Bar = 0, Tick = 0, Numerator = 4, Denominator = 4 }];
        }

        foreach (var beat in beats)
        {
            bw.Write(Encoding.ASCII.GetBytes("beat"));
            WriteIntFieldValue(bw, beat.Bar);
            WriteIntFieldValue(bw, beat.Numerator);
            WriteIntFieldValue(bw, beat.Denominator);
            bw.Write(0);
        }

        foreach (var bpm in _chart.Events.Children.OfType<UmgrModel.BpmEvent>().OrderBy(x => x.Tick).DefaultIfEmpty(
                     new UmgrModel.BpmEvent { Tick = 0, Bpm = _chart.Meta.MainBpm > 0 ? _chart.Meta.MainBpm : 120m }))
        {
            bw.Write(Encoding.ASCII.GetBytes("bpm "));
            WriteIntFieldValue(bw, bpm.Tick.Original);
            WriteDoubleFieldValue(bw, bpm.Bpm);
            bw.Write(0);
        }

        foreach (var speed in _chart.Events.Children.OfType<UmgrModel.NoteSpeedEvent>().OrderBy(x => x.Tick))
        {
            bw.Write(Encoding.ASCII.GetBytes("smod"));
            WriteIntFieldValue(bw, speed.Tick.Original);
            WriteDoubleFieldValue(bw, speed.Speed);
            bw.Write(0);
        }

        foreach (var til in _chart.Events.Children.OfType<UmgrModel.ScrollSpeedEvent>()
                     .OrderBy(x => x.Timeline).ThenBy(x => x.Tick))
        {
            bw.Write(Encoding.ASCII.GetBytes("til "));
            WriteIntFieldValue(bw, til.Timeline);
            WriteIntFieldValue(bw, til.Tick.Original);
            WriteDoubleFieldValue(bw, til.Speed);
            bw.Write(0);
        }
    }

    private void WriteNotes(BinaryWriter bw)
    {
        _writtenSlides.Clear();
        _writtenCurvePaths.Clear();
        _writtenExTapRoles.Clear();
        _chart.Extras.AirAppearances.Clear();
        foreach (var tick in _chart.Extras.ClickTicks)
        {
            WriteNote(bw, new NoteFormat(NoteType.Click, LongAttr.None, Direction.None, ExAttr.None, 0),
                new NotePosition(0, 0, 0, tick, 0));
        }

        var effectCarrierPlans = BuildGeneratedEffectCarrierPlans();
        var writtenEffectCarriers = new HashSet<EffectCarrierKey>();

        foreach (var note in _chart.Notes.Children)
        {
            switch (note)
            {
                case UmgrModel.Tap tap:
                    WriteNote(bw, new NoteFormat(NoteType.Tap, LongAttr.None, Direction.None, ExAttr.None, 0),
                        new NotePosition(tap.Lane, tap.Width, DefaultHeight, tap.Tick.Original, tap.Timeline));
                    WritePairedAirActionIfNeeded(bw, tap);
                    break;
                case UmgrModel.ExTap
                {
                    Role: UmgrModel.ExTapRole.AirActionCarrier
                }:
                    break;
                case UmgrModel.ExTap ex:
                    WriteExTapNote(bw, ex);
                    break;
                case UmgrModel.Flick flick:
                    WriteNote(bw, new NoteFormat(NoteType.Flick, LongAttr.None,
                            flick.AutoplayDirection switch { 'L' => Direction.Left, 'R' => Direction.Right, _ => Direction.Auto }, ExAttr.None, 0),
                        new NotePosition(flick.Lane, flick.Width, DefaultHeight, flick.Tick.Original, flick.Timeline));
                    WritePairedAirActionIfNeeded(bw, flick);
                    break;
                case UmgrModel.Damage damage:
                    WriteNote(bw, new NoteFormat(NoteType.Damage, LongAttr.None, Direction.None, ExAttr.None, 0),
                        new NotePosition(damage.Lane, damage.Width, DefaultHeight, damage.Tick.Original, damage.Timeline));
                    WritePairedAirActionIfNeeded(bw, damage);
                    break;
                case UmgrModel.Hold hold:
                    WriteHoldNote(bw, hold, effectCarrierPlans, writtenEffectCarriers);
                    break;
                case UmgrModel.Slide slide:
                    WriteSlideNote(bw, slide, effectCarrierPlans, writtenEffectCarriers);
                    break;
                case UmgrModel.Air air:
                    WriteAirNote(bw, air);
                    break;
                case UmgrModel.AirHold airHold:
                    WriteAirHoldNote(bw, airHold);
                    break;
                case UmgrModel.AirSlide airSlide:
                    WriteAirSlideNote(bw, airSlide);
                    break;
                case UmgrModel.AirCrash crash:
                    WriteAirCrashNote(bw, crash);
                    break;
            }
        }
    }

    private void WriteExTapNote(BinaryWriter bw, UmgrModel.ExTap ex)
    {
        _writtenExTapRoles.Add(RoleOf(ex));
        WriteNote(bw, new NoteFormat(NoteType.ExTap, LongAttr.None, EffectDirection(ex.Effect), ExAttr.None, 0),
            new NotePosition(ex.Lane, ex.Width, DefaultHeight, ex.Tick.Original, ex.Timeline));

        WritePairedAirActionIfNeeded(bw, ex);
    }

    private void WriteHoldNote(BinaryWriter bw, UmgrModel.Hold hold, IReadOnlyDictionary<EffectCarrierKey, EffectCarrierPlan> effectCarrierPlans,
        HashSet<EffectCarrierKey> writtenEffectCarriers)
    {
        WriteExCarrierIfNeeded(bw, hold, effectCarrierPlans, writtenEffectCarriers);
        WriteNote(bw, new NoteFormat(NoteType.Hold, LongAttr.Begin, Direction.None, ExAttr.None, 0),
            new NotePosition(hold.Lane, hold.Width, DefaultHeight, hold.Tick.Original, hold.Timeline));

        var holdJoints = hold.Children.OfType<UmgrModel.HoldJoint>().ToArray();
        for (var i = 0; i < holdJoints.Length; i++)
        {
            var joint = holdJoints[i];
            WriteNote(bw, new NoteFormat(NoteType.Hold, LongAttr.End, Direction.None, ExAttr.None, 0),
                new NotePosition(hold.Lane, hold.Width, DefaultHeight, joint.Tick.Original, joint.Timeline));

            if (i == holdJoints.Length - 1)
            {
                WritePairedAirActionIfNeeded(bw, joint);
            }
        }
    }

    private void WriteSlideNote(BinaryWriter bw, UmgrModel.Slide slide, IReadOnlyDictionary<EffectCarrierKey, EffectCarrierPlan> effectCarrierPlans,
        HashSet<EffectCarrierKey> writtenEffectCarriers)
    {
        _writtenSlides.Add(slide);
        _writtenCurvePaths.Add(slide);
        WriteExCarrierIfNeeded(bw, slide, effectCarrierPlans, writtenEffectCarriers);
        WriteNote(bw, new NoteFormat(NoteType.Slide, LongAttr.Begin, Direction.None, ExAttr.None, slide.NoLine ? NoLineVariation : (sbyte)0),
            new NotePosition(slide.Lane, slide.Width, DefaultHeight, slide.Tick.Original, slide.Timeline));
        var joints = slide.Children.OfType<UmgrModel.SlideJoint>().ToArray();
        for (var i = 0; i < joints.Length; i++)
        {
            var joint = joints[i];
            var isLast = i == joints.Length - 1;

            // Ground SLIDE uses Control even at its tail. EndNoAct belongs to
            // AIR paths; native ground exporters can drop it or add a judgement.
            var attr = joint.IsCurveControl ? LongAttr.CurveControl
                : joint.Joint == Joint.C ? LongAttr.Control : SlideAttr(joint.Joint, isLast);
            WriteNote(bw, new NoteFormat(NoteType.Slide, attr, Direction.None, ExAttr.None, 0),
                new NotePosition(joint.Lane, joint.Width, DefaultHeight, joint.Tick.Original, joint.Timeline));

            if (isLast)
            {
                WritePairedAirActionIfNeeded(bw, joint);
            }
        }
    }

    private void WriteAirNote(BinaryWriter bw, UmgrModel.Air air)
    {
        if (air.PairNote is UmgrModel.ExTap
            {
                Role: UmgrModel.ExTapRole.AirActionCarrier
            })
        {
            WriteAirActionCarrier(bw, air);
            WriteAirBase(bw, air.Direction, air.Color, air);
            return;
        }

        if (HasAirActionAt(air) || air.PairNote is not null)
        {
            return;
        }

        WriteNote(bw, new NoteFormat(NoteType.Air, LongAttr.None, AirDir(air.Direction), AirEx(air.Color), 0),
            new NotePosition(air.Lane, air.Width, DefaultHeight, air.Tick.Original, air.Timeline));
    }

    private void WriteAirHoldNote(BinaryWriter bw, UmgrModel.AirHold airHold)
    {
        if (airHold.PairNote is not null &&
            airHold.PairNote is not UmgrModel.ExTap
            {
                Role: UmgrModel.ExTapRole.AirActionCarrier
            })
        {
            return;
        }

        WriteAirActionCarrier(bw, airHold);
        WriteAirHold(bw, airHold);
    }

    private void WriteAirSlideNote(BinaryWriter bw, UmgrModel.AirSlide airSlide)
    {
        if (airSlide.PairNote is not null &&
            airSlide.PairNote is not UmgrModel.ExTap
            {
                Role: UmgrModel.ExTapRole.AirActionCarrier
            })
        {
            return;
        }

        WriteAirActionCarrier(bw, airSlide);
        WriteAirSlide(bw, airSlide);
    }

    private void WriteAirCrashNote(BinaryWriter bw, UmgrModel.AirCrash crash)
    {
        var crashAxis = crash.Attr switch { AirLadderAttr.AxisY => ExAttr.AxisY, AirLadderAttr.AxisZ => ExAttr.AxisZ, _ => ExAttr.None };
        var direction = Version < 3 ? crash.Attr switch
        {
            AirLadderAttr.AxisY => Direction.RotateLeft,
            AirLadderAttr.AxisZ => Direction.RotateRight,
            _ => Direction.None
        } : Direction.None;
        WriteNote(bw, new NoteFormat(NoteType.AirCrush, LongAttr.Begin, direction,
                crash.HasExplicitJudgements ? ExAttr.HasNote : Version < 3 ? ExAttr.None : crashAxis, CrushVariation(crash.Color)),
            new NotePosition(crash.Lane, crash.Width, Height(crash.Height), crash.Tick.Original, crash.Timeline), crash.Density.Original);

        var crashJoints = crash.Children.OfType<UmgrModel.AirCrashJoint>().ToArray();
        for (var i = 0; i < crashJoints.Length; i++)
        {
            var joint = crashJoints[i];
            var longAttr = i == crashJoints.Length - 1
                ? LongAttr.End
                : crash.HasExplicitJudgements && joint.IsJudgement ? LongAttr.Step : LongAttr.Control;

            WriteNote(bw, new NoteFormat(NoteType.AirCrush, longAttr, Direction.None,
                    crash.HasExplicitJudgements && joint.IsJudgement ? ExAttr.HasNote : ExAttr.None, 0),
                new NotePosition(joint.Lane, joint.Width, Height(joint.Height), joint.Tick.Original, joint.Timeline));
        }
    }

    private void WriteExCarrierIfNeeded(
        BinaryWriter bw,
        UmgrModel.ExTapableNote note,
        IReadOnlyDictionary<EffectCarrierKey, EffectCarrierPlan> plans,
        HashSet<EffectCarrierKey> written)
    {
        if (note.Effect is null)
        {
            return;
        }

        if (HasEffectCarrier(note))
        {
            return;
        }

        var key = EffectCarrierKeyOf(note);
        if (!plans.TryGetValue(key, out var plan))
        {
            return;
        }

        if (!written.Add(key))
        {
            return;
        }

        _writtenExTapRoles.Add(new(plan.Role, UmgrModel.AirActionCarrierParent.None, Joint.D, false));
        WriteNote(bw, new NoteFormat(NoteType.ExTap, LongAttr.None, EffectDirection(plan.Effect), ExAttr.None, 0),
            new NotePosition(note.Lane, note.Width, DefaultHeight, note.Tick.Original, plan.Timeline));
    }

    private static EffectCarrierKey EffectCarrierKeyOf(UmgrModel.Note note) =>
        new(note.Tick.Original, note.Lane, note.Width);

    private Dictionary<EffectCarrierKey, EffectCarrierPlan> BuildGeneratedEffectCarrierPlans()
    {
        return _chart.Notes.Children
            .OfType<UmgrModel.ExTapableNote>()
            .Where(note => note.Effect is not null && !HasEffectCarrier(note))
            .GroupBy(EffectCarrierKeyOf)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var selected = group.OfType<UmgrModel.Slide>().FirstOrDefault() ?? group.First();

                    return new EffectCarrierPlan(
                        selected.Effect ?? ExEffect.UP,
                        selected.Timeline,
                        selected is UmgrModel.Slide
                            ? UmgrModel.ExTapRole.SharedLongCarrier
                            : UmgrModel.ExTapRole.HoldOnlyCarrier);
                });
    }

    private void WritePairedAirActionIfNeeded(
        BinaryWriter bw,
        UmgrModel.PositiveNote parent)
    {
        switch (parent.PairNote)
        {
            case UmgrModel.AirHold airHold:
                WriteAirHold(bw, airHold);
                break;

            case UmgrModel.AirSlide airSlide:
                WriteAirSlide(bw, airSlide);
                break;

            default:
                WritePairedAirIfNeeded(bw, parent);
                break;
        }
    }

    private void WritePairedAirIfNeeded(
        BinaryWriter bw,
        UmgrModel.PositiveNote parent)
    {
        if (parent.PairNote is not UmgrModel.Air air)
        {
            return;
        }

        if (HasAirActionAt(air))
        {
            return;
        }

        WriteNote(bw, new NoteFormat(NoteType.Air, LongAttr.None, AirDir(air.Direction), AirEx(air.Color), 0),
            new NotePosition(air.Lane, air.Width, DefaultHeight, air.Tick.Original, air.Timeline));
    }

    private void WriteAirHold(
        BinaryWriter bw,
        UmgrModel.AirHold airHold)
    {
        _chart.Extras.AirAppearances.Add(new AirAppearance(ChartExtras.AirKey(airHold), airHold.Color, airHold.Direction));
        WriteAirBase(bw, airHold.Direction, airHold.Color, airHold);
        WriteNote(bw, new NoteFormat(NoteType.AirHold, LongAttr.Begin, Direction.Up, AirEx(airHold.Color), 0),
            new NotePosition(airHold.Lane, airHold.Width, DefaultHeight, airHold.Tick.Original, airHold.Timeline));

        var airHoldJoints =
            airHold.Children.OfType<UmgrModel.AirHoldJoint>().ToArray();

        for (var i = 0; i < airHoldJoints.Length; i++)
        {
            var joint = airHoldJoints[i];

            WriteNote(bw, new NoteFormat(NoteType.AirHold, SlideAttr(
                    joint.Joint,
                    i == airHoldJoints.Length - 1), Direction.None, ExAttr.None, 0),
            new NotePosition(airHold.Lane, airHold.Width, DefaultHeight, joint.Tick.Original, joint.Timeline));
        }
    }

    private void WriteAirSlide(
        BinaryWriter bw,
        UmgrModel.AirSlide airSlide)
    {
        _writtenCurvePaths.Add(airSlide);
        _chart.Extras.AirAppearances.Add(new AirAppearance(ChartExtras.AirKey(airSlide), airSlide.Color, airSlide.Direction));
        WriteAirBase(bw, airSlide.Direction, airSlide.Color, airSlide);
        WriteNote(bw, new NoteFormat(NoteType.AirSlide, LongAttr.Begin, Direction.Up, AirEx(airSlide.Color), 0),
            new NotePosition(airSlide.Lane, airSlide.Width, Height(airSlide.Height), airSlide.Tick.Original, airSlide.Timeline));

        var airSlideJoints =
            airSlide.Children.OfType<UmgrModel.AirSlideJoint>().ToArray();

        for (var i = 0; i < airSlideJoints.Length; i++)
        {
            var joint = airSlideJoints[i];

            WriteNote(bw, new NoteFormat(NoteType.AirSlide, joint.IsCurveControl ? LongAttr.CurveControl : SlideAttr(
                    joint.Joint,
                    i == airSlideJoints.Length - 1), Direction.None, ExAttr.None, 0),
            new NotePosition(joint.Lane, joint.Width, Height(joint.Height), joint.Tick.Original, joint.Timeline));
        }
    }

    private void WriteAirActionCarrier(
        BinaryWriter bw,
        UmgrModel.NegativeNote action)
    {
        if (action.PairNote is not UmgrModel.ExTap
            {
                Role: UmgrModel.ExTapRole.AirActionCarrier
            } carrier)
        {
            return;
        }

        _writtenExTapRoles.Add(RoleOf(carrier));

        WriteNote(bw, new NoteFormat(NoteType.ExTap, LongAttr.None, EffectDirection(carrier.Effect), ExAttr.None, 0),
            new NotePosition(carrier.Lane, carrier.Width, DefaultHeight, carrier.Tick.Original, carrier.Timeline));
    }

    private static ExTapRoleSnapshot RoleOf(UmgrModel.ExTap note) =>
        new(note.Role, note.AirActionParent, note.AirActionParentJoint, note.AirActionParentIsEx);

    private void WriteAirBase(
        BinaryWriter bw,
        AirDirection direction,
        Color color,
        UmgrModel.NegativeNote action)
    {
        WriteNote(bw, new NoteFormat(NoteType.Air, LongAttr.None, AirDir(direction), AirEx(color), 0),
            new NotePosition(action.Lane, action.Width, DefaultHeight, action.Tick.Original, action.Timeline));
    }

    private static bool HasAirActionAt(UmgrModel.Air air) =>
        air.PairNote?.PairNote is UmgrModel.AirHold or UmgrModel.AirSlide;

    private bool HasEffectCarrier(UmgrModel.Note note) =>
        _chart.Notes.Children.OfType<UmgrModel.ExTap>().Any(x =>
            x.Role != UmgrModel.ExTapRole.Explicit &&
            (x.Role != UmgrModel.ExTapRole.HoldOnlyCarrier || note is UmgrModel.Hold) &&
            x.Tick == note.Tick &&
            x.Lane <= note.Lane &&
            x.Lane + x.Width >= note.Lane + note.Width);

    private readonly record struct NoteFormat(
        NoteType Type, LongAttr LongAttr, Direction Direction, ExAttr ExAttr, sbyte VariationId);

    private readonly record struct NotePosition(int Lane, int Width, int NativeHeight, int Tick, int Timeline);

    private void WriteNote(BinaryWriter bw, NoteFormat format, NotePosition position, int? optionValue = null)
    {
        var (type, longAttr, direction, exAttr, variationId) = format;
        var (lane, width, height, tick, timeline) = position;
        bw.Write((sbyte)type);
        bw.Write((sbyte)longAttr);
        bw.Write((sbyte)direction);
        bw.Write((sbyte)exAttr);
        bw.Write(variationId);
        if (Version < 3)
        {
            bw.Write(checked((sbyte)lane));
            bw.Write(checked((short)width));
            bw.Write(height);
        }
        else
        {
            bw.Write((byte)0);
            bw.Write(checked((byte)width));
            bw.Write(checked((sbyte)lane));
            if (request.Target == EditorTarget.Extended)
            {
                bw.Write(height);
            }
            else
            {
                bw.Write(checked((ushort)height));
                bw.Write((ushort)0);
            }
        }
        bw.Write(tick);
        bw.Write(timeline);
        if (optionValue is { } option)
        {
            bw.Write(option);
        }
    }

    private static LongAttr SlideAttr(Joint joint, bool isLast) => (joint, isLast) switch
    {
        (Joint.D, true) => LongAttr.End,
        (Joint.C, true) => LongAttr.EndNoAct,
        (Joint.D, false) => LongAttr.Step,
        _ => LongAttr.Control
    };

    private static Direction EffectDirection(ExEffect effect) => effect switch
    {
        ExEffect.UP => Direction.Up,
        ExEffect.DW => Direction.Down,
        ExEffect.CE => Direction.Center,
        ExEffect.LS => Direction.Left,
        ExEffect.RS => Direction.Right,
        ExEffect.LC => Direction.RotateLeft,
        ExEffect.RC => Direction.RotateRight,
        ExEffect.BS => Direction.InOut,
        _ => Direction.Up
    };

    private static Direction AirDir(AirDirection direction) => direction switch
    {
        AirDirection.IR => Direction.Up,
        AirDirection.DW => Direction.Down,
        AirDirection.UL => Direction.UpLeft,
        AirDirection.UR => Direction.UpRight,
        AirDirection.DL => Direction.DownLeft,
        AirDirection.DR => Direction.DownRight,
        _ => Direction.Up
    };


    private static ExAttr AirEx(Color color) => color is Color.PNK ? ExAttr.Invert : ExAttr.None;

    private static sbyte CrushVariation(Color color) => color switch
    {
        Color.DEF => 0,
        Color.RED => 1,
        Color.ORN => 2,
        Color.YEL => 3,
        Color.GRN => 4,
        Color.AQA => 5,
        Color.BLU => 6,
        Color.PPL => 7,
        Color.VLT => 8,
        Color.GRY => 10,
        Color.BLK => 11,
        Color.LIM => 12,
        Color.CYN => 13,
        Color.DGR => 14,
        Color.PNK => 15,
        Color.NON => 35,
        _ => 0
    };

    private static int Height(decimal value) => (int)Math.Round(value);

    private static int DifficultyValue(Difficulty d) => d switch
    {
        Difficulty.WorldsEnd => 4,
        Difficulty.Ultima => 5,
        _ => (int)d
    };

    internal static string WeLevel(StarDifficulty star) => star switch
    {
        StarDifficulty.S1 => "1",
        StarDifficulty.S2 => "2",
        StarDifficulty.S3 => "3",
        StarDifficulty.S4 => "4",
        StarDifficulty.S5 => "5",
        _ => ""
    };

    internal static string FormatPlayLevel(decimal level)
    {
        if (level <= 0)
        {
            return "";
        }

        var whole = (int)decimal.Truncate(level);
        return level - whole >= 0.5m ? $"{whole}+" : whole.ToString(CultureInfo.InvariantCulture);
    }

    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MGXC bookmark IDs use a deterministic legacy MD5 identifier for format compatibility, not for security.")]
    private static void WriteBookmark(
        BinaryWriter bw,
        int tick,
        string tag,
        string rgb,
        string? id = null)
    {
        bw.Write(Encoding.ASCII.GetBytes("bmrk"));
        WriteWideField(
            bw,
            string.IsNullOrWhiteSpace(id)
                ? Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(tag)))
                : id);
        WriteIntFieldValue(bw, tick);
        WriteWideField(bw, tag);
        WriteWideField(bw, rgb);
        bw.Write(0);
    }

    private static void WriteWideField(BinaryWriter bw, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        bw.Write(4);
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static void WriteStringField(BinaryWriter bw, string name, string value)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        if (bytes.Length > ushort.MaxValue)
        {
            throw new DiagnosticException(Msg.Create(MsgKeys.MgCrit_Mgxc_string_too_long,
                name, bytes.Length, ushort.MaxValue));
        }
        bw.Write((short)4);
        bw.Write((ushort)bytes.Length);
        bw.Write(bytes);
    }

    private static void WriteIntField(BinaryWriter bw, string name, int value)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        WriteIntFieldValue(bw, value);
    }

    private static void WriteIntFieldValue(BinaryWriter bw, int value)
    {
        if (value is >= short.MinValue and <= short.MaxValue)
        {
            bw.Write((short)1);
            bw.Write((short)value);
            return;
        }

        bw.Write((short)2);
        bw.Write((short)0);
        bw.Write(value);
    }

    private static void WriteDoubleField(BinaryWriter bw, string name, decimal value)
    {
        bw.Write(Encoding.ASCII.GetBytes(name));
        WriteDoubleFieldValue(bw, value);
    }

    private static void WriteDoubleFieldValue(BinaryWriter bw, decimal value)
    {
        bw.Write((short)3);
        bw.Write((short)0);
        bw.Write(NativeNumber.ToDouble(value, preserveSingle: false));
    }

    private enum NoteType : sbyte
    {
        Tap = 0x01,
        ExTap = 0x02,
        Flick = 0x03,
        Damage = 0x04,
        Hold = 0x05,
        Slide = 0x06,
        Air = 0x07,
        AirHold = 0x08,
        AirSlide = 0x09,
        AirCrush = 0x0A,
        Click = 0x0B
    }

    private enum LongAttr : sbyte
    {
        None = 0x00,
        Begin = 0x01,
        Step = 0x02,
        Control = 0x03,
        CurveControl = 0x04,
        End = 0x05,
        EndNoAct = 0x06
    }

    private enum Direction : sbyte
    {
        None = 0x00,
        Auto = 0x01,
        Up = 0x02,
        Down = 0x03,
        Center = 0x04,
        Left = 0x05,
        Right = 0x06,
        UpLeft = 0x07,
        UpRight = 0x08,
        DownLeft = 0x09,
        DownRight = 0x0A,
        RotateLeft = 0x0B,
        RotateRight = 0x0C,
        InOut = 0x0D
    }

    private enum ExAttr : sbyte
    {
        None = 0x00,
        Invert = 0x01,
        HasNote = 0x02,
        AxisY = 0x04,
        AxisZ = 0x05
    }
}

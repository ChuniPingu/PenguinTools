using PenguinTools.Chart.Diagnostics;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Diagnostic;

using C2sModel = PenguinTools.Chart.Models.c2s;
using UmgrModel = PenguinTools.Chart.Models.umgr;

namespace PenguinTools.Chart.Converter.c2s;

public partial class C2SChartConverter
{
    public C2SChartConverter(C2SConvertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Mgxc);

        Mgxc = ChartCopy.Create(request.Mgxc);
    }

    private DiagnosticCollector Diagnostic { get; } = new();
    private UmgrModel.Chart Mgxc { get; }
    private C2sModel.Chart C2s { get; } = new();
    private List<C2sModel.Note> Notes => C2s.Notes;
    private List<C2sModel.Event> Events => C2s.Events;
    private int _appliedBgmBarOffset;

    private bool RestoreSlaSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sSlaSnapshot;

        if (snapshot is null)
        {
            return false;
        }

        var currentSlaKey = C2sRoundTripKeys.FormatSlaEditKey(Mgxc);
        if (!(Mgxc.Extras.BinarySnapshotValid && Mgxc.Extras.SlaModelKey == currentSlaKey) &&
            Mgxc.Meta.C2sSlaEditKey != currentSlaKey)
        {
            Mgxc.Meta.C2sSlaSnapshot = null;
            Mgxc.Meta.C2sSlaEditKey = null;
            return false;
        }

        if (snapshot.Length == 0)
        {
            return true;
        }

        var restored = new List<C2sModel.Sla>();

        foreach (var entry in snapshot.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry.Split(',');

            if (fields.Length != 5 ||
                !int.TryParse(fields[0], out var tick) ||
                !int.TryParse(fields[1], out var timeline) ||
                !int.TryParse(fields[2], out var lane) ||
                !int.TryParse(fields[3], out var width) ||
                !int.TryParse(fields[4], out var length))
            {
                return false;
            }

            restored.Add(new C2sModel.Sla
            {
                Tick = tick,
                Timeline = timeline,
                Lane = lane,
                Width = width,
                Length = length
            });
        }

        foreach (var sla in restored)
        {
            Notes.Add(sla);
        }

        return true;
    }

    private void RestoreSlpSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sSlpSnapshot;

        if (snapshot is null)
        {
            return;
        }

        if (Mgxc.Meta.C2sSlpEditKey != C2sRoundTripKeys.FormatSlpEditKey(Mgxc))
        {
            Mgxc.Meta.C2sSlpSnapshot = null;
            Mgxc.Meta.C2sSlpEditKey = null;
            return;
        }

        var restored = new List<C2sModel.Slp>();

        if (snapshot.Length != 0)
        {
            foreach (var entry in snapshot.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = entry.Split(',');

                if (fields.Length != 4 ||
                    !int.TryParse(fields[0], out var tick) ||
                    !int.TryParse(fields[1], out var timeline) ||
                    !int.TryParse(fields[2], out var length) ||
                    !decimal.TryParse(
                        fields[3],
                        System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var speed))
                {
                    return;
                }

                restored.Add(new C2sModel.Slp
                {
                    Tick = tick,
                    Timeline = timeline,
                    Length = length,
                    Speed = speed
                });
            }
        }

        Events.RemoveAll(x => x is C2sModel.Slp);
        Events.AddRange((IEnumerable<C2sModel.Event>)restored);

    }

    private void RestoreAirSnapshot()
    {
        var snapshot = Mgxc.Meta.C2sAirSnapshot;

        if (snapshot is null)
        {
            return;
        }

        var currentAirKey = C2sRoundTripKeys.FormatAirEditKey(Mgxc);
        // Missing validation metadata cannot authorize replacing the edited AIRs.
        var unchanged = Mgxc.Extras.BinarySnapshotValid && Mgxc.Extras.AirModelKey == currentAirKey ||
            Mgxc.Meta.C2sAirEditKey == currentAirKey;
        if (!unchanged)
        {
            Mgxc.Meta.C2sAirSnapshot = null;
            Mgxc.Meta.C2sAirEditKey = null;
            return;
        }

        var restored = new List<C2sModel.Air>();
        var existing = Notes.OfType<C2sModel.Air>().ToList();

        foreach (var entry in snapshot.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var air = RestoreAirSnapshotEntry(entry, existing);
            if (air is null)
            {
                return;
            }
            restored.Add(air);
        }

        Notes.RemoveAll(x => x is C2sModel.Air);
        Notes.AddRange((IEnumerable<C2sModel.Note>)restored);

    }

    private static C2sModel.Air? RestoreAirSnapshotEntry(string entry, List<C2sModel.Air> existing)
    {
        var fields = entry.Split(',');
        if (fields.Length != 7 ||
            !int.TryParse(fields[0], out var tick) ||
            !int.TryParse(fields[1], out var timeline) ||
            !int.TryParse(fields[2], out var lane) ||
            !int.TryParse(fields[3], out var width) ||
            !Enum.TryParse<AirDirection>(fields[4], out var direction) ||
            !Enum.TryParse<Color>(fields[5], out var color))
        {
            return null;
        }
        var parent = CreateAirSnapshotParent(fields[6]);
        if (parent is null)
        {
            return null;
        }

        // C2S has no per-AIR timeline field. Its snapshot predates SLA
        // projection, which may assign a timeline to the editor note.
        var air = existing.FirstOrDefault(a => a.Tick.Original == tick &&
            a.Lane == lane && a.Width == width && a.Parent?.GetType() == parent.GetType() &&
            a.Direction == direction && a.Color == color);
        air ??= existing.FirstOrDefault(a => a.Tick.Original == tick &&
            a.Lane == lane && a.Width == width && a.Parent?.GetType() == parent.GetType());
        if (air is not null)
        {
            existing.Remove(air);
        }
        else
        {
            air = new C2sModel.Air { Tick = tick, Timeline = timeline, Lane = lane, Width = width, Parent = parent };
        }
        air.Direction = direction;
        air.Color = color;
        air.Timeline = timeline;
        return air;
    }

    private static C2sModel.Note? CreateAirSnapshotParent(string id) =>
        id switch
        {
            "TAP" => new C2sModel.Tap(),
            "CHR" => new C2sModel.ExTap(),
            "MNE" => new C2sModel.Damage(),
            "FLK" => new C2sModel.Flick(),

            "HLD" => new C2sModel.Hold(),
            "HXD" => new C2sModel.Hold
            {
                Effect = ExEffect.UP
            },

            "SLC" => new C2sModel.Slide
            {
                Joint = Joint.C
            },
            "SLD" => new C2sModel.Slide
            {
                Joint = Joint.D
            },
            "SXC" => new C2sModel.Slide
            {
                Joint = Joint.C,
                Effect = ExEffect.UP
            },
            "SXD" => new C2sModel.Slide
            {
                Joint = Joint.D,
                Effect = ExEffect.UP
            },

            _ => null
        };

    private void RestoreMeterDefSnapshot()
    {
        if (Mgxc.Extras.MeterEditKey is { } key && key != ChartExtras.BeatKey(Mgxc))
        {
            return;
        }

        if (Mgxc.Meta.C2sMeterDefDenominator is { } denominator)
        {
            C2s.Meta.BgmInitialDenominator = denominator;
        }

        if (Mgxc.Meta.C2sMeterDefNumerator is { } numerator)
        {
            C2s.Meta.BgmInitialNumerator = numerator;
        }
    }

    public OperationResult<C2sModel.Chart> Convert()
    {
        Diagnostic.TimeCalculator = Mgxc.GetCalculator();
        try
        {
            NoteWidthRules.Validate(Mgxc);
            Mgxc.Extras.CheckLegacyColors(Mgxc);
            CurveExpansion.Expand(Mgxc);
            UgcSpeedFields.WarnIfUnsupported(Mgxc, "C2S", Diagnostic);
            C2s.Meta = Mgxc.Meta;
            C2s.Meta.BgmFixedZeroOrigin |= Mgxc.Extras.UsesFixedCoordinateOrigin;
            if (Mgxc.Extras.ParsedEventModelKey != C2SRoundTrip.OrderedViewHash(ChartExtras.EventView(Mgxc).Split('\n')))
            {
                Mgxc.Extras.UnchangedEventKinds.Clear();
                Mgxc.Extras.CheckEventView(Mgxc);
            }
            C2s.Extras = Mgxc.Extras;

            var restoredSla = RestoreSlaSnapshot();
            C2s.Extras.RebuiltSla = false;

            foreach (var note in Mgxc.Notes.Children)
            {
                if (note is UmgrModel.SoflanArea)
                {
                    continue;
                }

                ConvertNote(note);
            }
            ResolvePairings();
            RestoreAirSnapshot();
            ConvertEvent(Mgxc);
            if (C2s.Extras.MeterEditKey == ChartExtras.BeatKey(Mgxc))
            {
                Events.RemoveAll(e => e is C2sModel.Met);
                Events.AddRange(C2s.Extras.Meters.Select(m => new C2sModel.Met
                { Tick = m.Tick, Numerator = m.Numerator, Denominator = m.Denominator }));
            }

            RestoreSlpSnapshot();
            if (C2s.Extras.HasSpeedSnapshot && C2s.Extras.BinarySnapshotValid && C2s.Extras.SpeedModelKey == ChartExtras.SpeedKey(Mgxc))
            {
                Events.RemoveAll(e => e is C2sModel.SpeedEventBase);
                foreach (var saved in C2s.Extras.Speeds)
                {
#pragma warning disable CS0612
                    C2sModel.SpeedEventBase restored = saved.Tag switch
                    {
                        "SLP" => new C2sModel.Slp { Timeline = saved.Timeline },
                        "SFL" => new C2sModel.Sfl(),
                        "STP" => new C2sModel.Stop(),
                        _ => new C2sModel.Dcm()
                    };
#pragma warning restore CS0612
                    restored.Tick = saved.Tick;
                    restored.Length = saved.Length;
                    restored.Speed = saved.Speed;
                    Events.Add(restored);
                }
            }
            RestoreMeterDefSnapshot();
            ApplyBgmBarOffset();
            var scheduled = C2sAirScheduler.Schedule(Notes, _slidePaths);
            Notes.Clear();
            Notes.AddRange(scheduled);
            if (!restoredSla)
            {
                AssignSoflanTimelines();
            }
            ValidateLongNoteLengths();
            NoteWidthRules.Validate(C2s);

            return ValidatePairings()
                ? OperationResult<C2sModel.Chart>.Success(C2s).WithDiagnostics(Diagnostic)
                : OperationResult<C2sModel.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
        catch (DiagnosticException ex)
        {
            Diagnostic.Report(ex);
            return OperationResult<C2sModel.Chart>.Failure().WithDiagnostics(Diagnostic);
        }
    }

    private void ApplyBgmBarOffset()
    {
        // C2S imports already use the game's coordinate origin, even after edits
        // invalidate their snapshots. Recognize older saved imports as well.
        if (!Mgxc.Meta.BgmEnableBarOffset || C2s.Extras.UsesFixedCoordinateOrigin)
        {
            return;
        }

        var offset = (int)Math.Round((decimal)ChartResolution.UmiguriTick /
            Mgxc.Meta.BgmInitialDenominator * Mgxc.Meta.BgmInitialNumerator);
        _appliedBgmBarOffset = offset;
        C2s.Extras = C2s.Extras.WithClickOffset(offset);
        C2s.Extras.PrerollBar1Applied |= offset > 0;
        foreach (var e in Events)
        {
            // Original initial speeds cover the preroll, but their ends still move with the notes.
            if (e.Tick.Original == 0 && e is C2sModel.SpeedEventBase speed)
            {
                speed.Length = checked(speed.Length.Original + offset);
            }
            // Keep the initial tempo and meter active throughout the preroll.
            else if (e.Tick.Original != 0 || e is C2sModel.SpeedEventBase)
            {
                e.Tick = checked(e.Tick.Original + offset);
            }
        }

        foreach (var note in Notes)
        {
            note.Tick = checked(note.Tick.Original + offset);
            if (note is C2sModel.LongNote longNote)
            {
                longNote.EndTick = checked(longNote.EndTick.Original + offset);
            }
        }
    }

    private void ValidateLongNoteLengths()
    {
        foreach (var longNote in Notes.OfType<C2sModel.LongNote>())
        {
            var length = longNote.Length.Original;
            if (length >= ChartResolution.SingleTick)
            {
                continue;
            }

            // Validate exported lengths, but locate diagnostics in the source chart.
            var tick = longNote.Tick.Original - _appliedBgmBarOffset;
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Length_smaller_than_unit, length,
                ChartResolution.UmiguriTick / ChartResolution.SingleTick);
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, tick)
            {
                Target = NoteSegmentDiagnosticTarget.From(longNote, _appliedBgmBarOffset, Diagnostic.TimeCalculator)
            });
        }

        foreach (var sla in Notes.OfType<C2sModel.Sla>())
        {
            var end = new Time(checked(sla.Tick.Original + sla.Length.Original));
            var length = end.Round - sla.Tick.Round;
            if (length >= ChartResolution.SingleTick)
            {
                continue;
            }

            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Length_smaller_than_unit, length,
                ChartResolution.UmiguriTick / ChartResolution.SingleTick);
            Diagnostic.Report(new TimedDiagnostic(Severity.Warning, msg, sla.Tick.Original - _appliedBgmBarOffset)
            {
                Target = NoteSegmentDiagnosticTarget.From(sla, _appliedBgmBarOffset, Diagnostic.TimeCalculator)
            });
        }
    }

    private bool ValidatePairings()
    {
        var hasError = false;
        foreach (var air in Notes.OfType<C2sModel.Air>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(
                new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_parent_null),
                    air.Tick.Original - _appliedBgmBarOffset)
                {
                    Target = air
                });
            hasError = true;
        }

        foreach (var airSlide in Notes.OfType<C2sModel.AirSlide>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null),
                airSlide.Tick.Original - _appliedBgmBarOffset)
            {
                Target = airSlide
            });
            hasError = true;
        }

        foreach (var airHold in Notes.OfType<C2sModel.AirHold>().Where(a => a.Parent is null))
        {
            Diagnostic.Report(new TimedDiagnostic(Severity.Error, Msg.Key(MsgKeys.MgCrit_Air_slide_parent_null),
                airHold.Tick.Original - _appliedBgmBarOffset)
            {
                Target = airHold
            });
            hasError = true;
        }

        return !hasError;
    }
}

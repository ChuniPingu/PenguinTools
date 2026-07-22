using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PenguinTools.Chart.Models;
using PenguinTools.Core.Asset;
using PenguinTools.Core.Diagnostic;

namespace PenguinTools.Chart.Parser;

using umgr = Models.umgr;

internal sealed partial class ChartPostProcessor(umgr.Chart chart, IDiagnosticSink diag, AssetManager assets)
{
    private readonly Dictionary<int, List<umgr.Note>> _noteGroups = [];
    private readonly Dictionary<int, List<umgr.ScrollSpeedEvent>> _tilGroups = [];

    public void Run()
    {
        ProcessEvent();
        ProcessNote();
        ProcessTil();
        ProcessCommand();
    }

    public static string GetSortName(string? s)
    {
        if (s is null) return string.Empty;
        var t = s.ToUpperInvariant().Normalize(NormalizationForm.FormKC);
        t = WhitespaceRegex().Replace(t, "_");
        t = SpecialCharacterRegex().Replace(t, "");
        return t;
    }

    private void ProcessEvent()
    {
        var bpmEvents = chart.Events.Children.OfType<umgr.BpmEvent>().OrderBy(e => e.Tick).ToArray();
        if (bpmEvents.Length <= 0 || bpmEvents[0].Tick.Original != 0)
            throw new DiagnosticException(MsgKeys.Mg_Head_BPM_not_found);

        var beatEvents = chart.Events.Children.OfType<umgr.BeatEvent>().OrderBy(e => e.Bar).ToList();
        var firstBeatEvent = beatEvents.FirstOrDefault();
        if (firstBeatEvent is not { Bar: 0 })
        {
            var newEvent = new umgr.BeatEvent { Bar = 0, Numerator = 4, Denominator = 4 };
            chart.Events.InsertBefore(newEvent, firstBeatEvent);
            beatEvents.Insert(0, newEvent);
            diag.Report(new Diagnostic(Severity.Information, Msg.Key(MsgKeys.Mg_Head_Time_Signature_event_not_found)));
        }

        var initBeat = beatEvents[0];
        chart.Meta.BgmInitialBpm = bpmEvents[0].Bpm;
        chart.Meta.BgmInitialNumerator = initBeat.Numerator;
        chart.Meta.BgmInitialDenominator = initBeat.Denominator;

        umgr.Chart.CalculateBeatEventTicks(beatEvents);

        chart.Events.Sort();
    }

    private void ProcessNote()
    {
        if (chart.Notes.Children.Count <= 0) return;

        var noteGroup = chart.Notes.Children
            .OfType<umgr.ExTapableNote>()
            .GroupBy(note => note.Tick)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var exEffects = new Dictionary<Time, HashSet<ExEffect>>();
        var tbRemoved = new HashSet<umgr.ExTap>();

        foreach (var exTap in chart.Notes.Children.OfType<umgr.ExTap>())
        {
            if (!exEffects.TryGetValue(exTap.Tick, out var effectSet))
            {
                effectSet = [];
                exEffects[exTap.Tick] = effectSet;
            }

            effectSet.Add(exTap.Effect);

            if (!noteGroup.TryGetValue(exTap.Tick, out var notesAtTick)) continue;

            foreach (var note in notesAtTick)
            {
                var covering = exTap.Lane <= note.Lane && exTap.Lane + exTap.Width >= note.Lane + note.Width;
                if (!covering) continue;

                note.Effect = exTap.Effect;

                var overlapping = exTap.Lane == note.Lane && exTap.Width == note.Width;
                if (overlapping && exTap.Children.Count <= 0 && exTap.PairNote == null) tbRemoved.Add(exTap);
            }
        }

        foreach (var exTap in tbRemoved) chart.Notes.RemoveChild(exTap);

        chart.Notes.Sort();

        foreach (var (tick, effects) in exEffects)
        {
            if (effects.Count <= 1) continue;
            var str = string.Join(", ", effects.Select(e => e.ToString()));
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Concurrent_ex_effects, str);
            diag.Report(new TimedDiagnostic(Severity.Information, msg, tick.Original));
        }
    }

    // thanks to @tangent 90
    private void ProcessTil()
    {
        GroupEventByTimeline(chart.Events);
        GroupNoteByTimeline(chart.Notes);
        MoveMainTimeline(chart.Meta.MainTil);
        ClearEmptyGroups();
        // TODO: Find conflicting note, compare priority and put them in separate group (SLA with larger TIL => larger priority when applying on note)
        PlaceSoflanArea();
        FinalizeEvent();
        FindNoteViolations();

        _tilGroups.Clear();
        _noteGroups.Clear();
    }

    private void FinalizeEvent()
    {
        var noteSpeedMods = chart.Events.Children.OfType<umgr.NoteSpeedEvent>().ToArray();
        foreach (var e in chart.Events.Children.OfType<umgr.SpeedEventBase>().ToArray()) chart.Events.RemoveChild(e);
        foreach (var (tilId, events) in _tilGroups)
        foreach (var e in events)
        {
            var newEvent = new umgr.ScrollSpeedEvent
            {
                Tick = e.Tick,
                Timeline = tilId,
                Speed = e.Speed
            };
            chart.Events.AppendChild(newEvent);
        }

        foreach (var e in noteSpeedMods)
            chart.Events.AppendChild(e);
    }

    private void PlaceSoflanArea()
    {
        foreach (var tils in _tilGroups.Values.ToArray()) tils.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        var slaSet = new HashSet<(int Tick, int Timeline, int Lane, int Width)>();
        foreach (var (id, notes) in _noteGroups)
        {
            if (id == 0) continue;
            var events = _tilGroups[id];
            foreach (var note in notes)
            {
                note.Timeline = id;

                // magic optimization: when the crash is transparent, it is not necessary to add the SLA on the control joint
                if (note is umgr.AirCrashJoint
                    {
                        Parent: umgr.AirCrash { Color: Color.NON }, Density.Original: 0x7FFFFFFF or 0
                    }) continue;

                // find the speed that is just before the note
                var prevTil = events.Where(p => p.Tick.Original <= note.Tick.Original).OrderByDescending(p => p.Tick)
                    .FirstOrDefault();
                if (prevTil?.Speed is null) continue;
                if (slaSet.Contains((note.Tick.Original, id, note.Lane, note.Width))) continue;

                var head = new umgr.SoflanArea
                {
                    Tick = note.Tick,
                    Timeline = id,
                    Lane = note.Lane,
                    Width = note.Width
                };
                var tail = new umgr.SoflanAreaJoint
                {
                    Tick = note.Tick.Original + ChartResolution.SingleTick
                };

                slaSet.Add((note.Tick.Original, id, note.Lane, note.Width));
                head.AppendChild(tail);
                chart.Notes.AppendChild(head);
            }
        }
    }

    private void GroupEventByTimeline(umgr.Event events)
    {
        foreach (var til in events.Children.OfType<umgr.ScrollSpeedEvent>())
        {
            var timelineId = til.Timeline;
            CreateGroup(timelineId);
            _tilGroups[timelineId].Add(til);
        }
    }

    private void GroupNoteByTimeline(umgr.Note parent)
    {
        if (parent.Children.Count == 0) return;
        foreach (var note in parent.Children)
        {
            GroupNoteByTimeline(note);
            var timeline = note.Timeline;
            CreateGroup(timeline);
            _noteGroups[timeline].Add(note);
        }
    }

    private void MoveMainTimeline(int mainTil)
    {
        if (!_tilGroups.ContainsKey(mainTil))
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Main_timeline_not_found, chart.Meta.MainTil);
            diag.Report(new Diagnostic(Severity.Information, msg));
            return;
        }

        SwapGroup(mainTil, 0);
    }

    private void ClearEmptyGroups()
    {
        foreach (var (id, events) in _tilGroups.ToArray())
        {
            var mappedNotes = _noteGroups[id];
            var maxTick = mappedNotes.Select(p => p.Tick).Append(0).Max();
            if (mappedNotes.Count == 0 && chart.Notes.Children.Count > 0) _tilGroups.Remove(id);
            else if (events.Count > 0 && maxTick.Original > 0)
                events.RemoveAll(p => p.Tick.Original > maxTick.Original + ChartResolution.SingleTick);
        }

        foreach (var (id, notes) in _noteGroups.ToArray())
            if (notes.Count == 0)
                _noteGroups.Remove(id);
    }

    private void CreateGroup(int id)
    {
        if (!_tilGroups.ContainsKey(id)) _tilGroups[id] = [];
        if (!_noteGroups.ContainsKey(id)) _noteGroups[id] = [];
    }

    private void SwapGroup(int aId, int bId)
    {
        if (aId == bId) return;
        CreateGroup(aId);
        CreateGroup(bId);

        var aEvents = _tilGroups[aId];
        var bEvents = _tilGroups[bId];
        _tilGroups.Remove(aId);
        _tilGroups.Remove(bId);
        foreach (var e in aEvents) e.Timeline = bId;
        foreach (var e in bEvents) e.Timeline = aId;
        _tilGroups[aId] = bEvents;
        _tilGroups[bId] = aEvents;

        var aNotes = _noteGroups[aId];
        var bNotes = _noteGroups[bId];
        foreach (var n in aNotes) n.Timeline = bId;
        foreach (var n in bNotes) n.Timeline = aId;

        _noteGroups.Remove(aId);
        _noteGroups.Remove(bId);
        _noteGroups[aId] = bNotes;
        _noteGroups[bId] = aNotes;
    }

    private void FindNoteViolations()
    {
        var violations = new HashSet<umgr.Note>();
        var noteGroup = chart.Notes.Children.GroupBy(n => (n.Tick, n.Lane)).Where(g => g.Count() > 1);

        foreach (var group in noteGroup)
        {
            var notesInGroup = group.ToArray();
            for (var i = 0; i < notesInGroup.Length; i++)
            for (var j = i + 1; j < notesInGroup.Length; j++)
            {
                if (!notesInGroup[i].IsViolate(notesInGroup[j])) continue;
                violations.Add(notesInGroup[i]);
                violations.Add(notesInGroup[j]);
            }
        }

        foreach (var note in violations)
            diag.Report(new TimedDiagnostic(Severity.Warning, Msg.Key(MsgKeys.Mg_Note_overlapped_in_different_TIL),
                note.Tick.Original)
            {
                Target = note
            });
    }

    private void MetaEntryHandler(string name, string[] args, Action<Entry> setter, AssetType type)
    {
        if (args.Length is < 1 or > 2)
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Meta_Argument_count_min_one, name);
            diag.Report(new Diagnostic(Severity.Warning, msg)
            {
                Target = args
            });
            return;
        }

        if (args.Length >= 2)
        {
            var newId = int.TryParse(args[0], out var parsedId)
                ? parsedId
                : throw new DiagnosticException(MsgKeys.Mg_Meta_First_argument_must_int);
            var data = args.Length >= 3 ? args[2] : null;
            var newEntry = new Entry(newId, args[1], data ?? string.Empty);
            setter(newEntry);
            assets.DefineEntry(type, newEntry);
            return;
        }

        var value = args[0];
        var entry = int.TryParse(value, out var id) ? assets[type].FirstOrDefault(e => e.Id == id) : null;
        entry ??= assets[type].FirstOrDefault(e => e.Str.Equals(value, StringComparison.Ordinal));

        if (entry == null)
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_String_id_not_found, value, AssetTypeDisplayName(type));
            diag.Report(new Diagnostic(Severity.Information, msg)
            {
                Target = args
            });
        }
        else
        {
            setter(entry);
        }
    }

    private static string AssetTypeDisplayName(AssetType type) => type switch
    {
        AssetType.GenreNames => "genreNames",
        AssetType.FieldLines => "notesFieldLine",
        AssetType.StageNames => "stageName",
        AssetType.WeTagNames => "worldsEndTagName",
        _ => type.ToString()
    };

    private void MetaGenreHandler(string[] args)
    {
        MetaEntryHandler("genre", args, entry => chart.Meta.Genre = entry, AssetType.GenreNames);
    }

    private void MetaStageHandler(string[] args)
    {
        MetaEntryHandler("stage", args, Setter, AssetType.StageNames);

        void Setter(Entry entry)
        {
            chart.Meta.Stage = entry;
            chart.Meta.IsCustomStage = false;
        }
    }

    private void MetaFieldLineHandler(string[] args)
    {
        MetaEntryHandler("fline", args, entry => chart.Meta.NotesFieldLine = entry, AssetType.FieldLines);
    }

    private void MetaWeTagHandler(string[] args)
    {
        MetaEntryHandler("wetag", args, entry => chart.Meta.WeTag = entry, AssetType.WeTagNames);
    }

    private void MainHandler(string[] args)
    {
        chart.Meta.IsMain = args.Length < 1 || ParseBool(args[0]);
    }

    private void MetaDateHandler(string[] args)
    {
        if (args.Length < 1)
        {
            MessageDescriptor msg = Msg.Create(MsgKeys.Mg_Meta_Argument_count_min_one, "date");
            diag.Report(new Diagnostic(Severity.Warning, msg)
            {
                Target = args
            });
            return;
        }

        if (!DateTime.TryParseExact(args[0], "yyyyMMdd", null, DateTimeStyles.None, out var date))
        {
            diag.Report(new Diagnostic(Severity.Warning, Msg.Key(MsgKeys.Mg_Meta_Invalid_date))
            {
                Target = args
            });
            return;
        }

        chart.Meta.ReleaseDate = date;
    }

    private void MetaHandler(string[] args)
    {
        var (name, value) = (args[0], args[1..]);

        switch (name)
        {
            case "stage":
                MetaStageHandler(value);
                break;
            case "main":
                MainHandler(value);
                break;
            case "genre":
                MetaGenreHandler(value);
                break;
            case "fline":
                MetaFieldLineHandler(value);
                break;
            case "wetag":
                MetaWeTagHandler(value);
                break;
            case "date":
                MetaDateHandler(value);
                break;
            default:
                diag.Report(new Diagnostic(Severity.Warning, Msg.Create(MsgKeys.Mg_Meta_Unknown_tag, name))
                {
                    Target = args
                });
                break;
        }
    }

    private void ProcessCommand()
    {
        var config = new Dictionary<string, Action<string[]>>
        {
            {
                "meta", MetaHandler
            }
        };

        var lines = chart.Meta.Comment.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (!trimmedLine.StartsWith('#')) continue;

            var parts = trimmedLine[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var tagName = parts[0];
            var tagArgs = parts.Skip(1).ToArray();

            if (config.TryGetValue(tagName, out var handler))
                try
                {
                    handler(tagArgs);
                }
                catch (Exception ex)
                {
                    diag.Report(ex);
                }
            else
                diag.Report(new Diagnostic(Severity.Warning, Msg.Create(MsgKeys.Mg_Meta_Unknown_tag, tagName))
                {
                    Target = parts
                });
        }
    }

    private static bool ParseBool(string str)
    {
        var value = str.ToLowerInvariant();
        if (value is "true" or "1" or "yes") return true;
        if (value is "false" or "0" or "no") return false;
        var test = string.IsNullOrWhiteSpace(str);
        return test;
    }
}

internal sealed partial class ChartPostProcessor
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}_]")]
    private static partial Regex SpecialCharacterRegex();
}
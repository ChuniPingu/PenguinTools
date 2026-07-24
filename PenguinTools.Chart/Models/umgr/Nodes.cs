/*
   This model is based on the original implementation from:
   https://github.com/inonote/MargreteOnline
*/

using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace PenguinTools.Chart.Models.umgr;

public abstract class TimeNode<T> where T : TimeNode<T>
{
    protected List<T> ChildNodes { get; } = [];

    public IReadOnlyList<T> Children => ChildNodes;

    [JsonIgnore] public bool IsVirtual { get; protected set; }

    [JsonIgnore] public T? NextSibling { get; protected set; }
    [JsonIgnore] public T? Parent { get; protected set; }
    [JsonIgnore] public T? PreviousSibling { get; protected set; }

    [JsonIgnore] public T? FirstChild => ChildNodes.Count > 0 ? ChildNodes[0] : null;
    [JsonIgnore] public T? LastChild => ChildNodes.Count > 0 ? ChildNodes[^1] : null;

    public virtual Time Tick { get; set; }

    public T? AppendChild(T? newNode)
    {
        if (newNode is null) return null;

        var parent = newNode.Parent;
        if (parent != null && !parent.RemoveChild(newNode)) return null;

        newNode.Parent = (T)this;
        ChildNodes.Add(newNode);

        if (ChildNodes.Count > 1)
        {
            var last = ChildNodes[^2];
            last.NextSibling = newNode;
            newNode.PreviousSibling = last;
        }
        else
        {
            newNode.PreviousSibling = null;
        }

        newNode.NextSibling = null;

        return newNode;
    }

    public T? InsertBefore(T newNode, T? referenceNode)
    {
        if (ReferenceEquals(newNode, null)) return null;
        if (referenceNode == null) return AppendChild(newNode);

        var parent = newNode.Parent;
        if (parent != null && !parent.RemoveChild(newNode)) return null;

        var beforeIndex = ChildNodes.IndexOf(referenceNode);
        if (beforeIndex == -1) return AppendChild(newNode);

        newNode.Parent = (T)this;
        ChildNodes.Insert(beforeIndex, newNode);

        if (beforeIndex > 0)
        {
            var prev = ChildNodes[beforeIndex - 1];
            prev.NextSibling = newNode;
            newNode.PreviousSibling = prev;
        }
        else
        {
            newNode.PreviousSibling = null;
        }

        newNode.NextSibling = referenceNode;
        referenceNode.PreviousSibling = newNode;

        return newNode;
    }

    public bool RemoveChild(T? child)
    {
        if (child is null) return false;

        var childIndex = ChildNodes.IndexOf(child);
        if (childIndex == -1) return false;

        if (childIndex > 0)
        {
            var prev = ChildNodes[childIndex - 1];
            prev.NextSibling = childIndex + 1 < ChildNodes.Count ? ChildNodes[childIndex + 1] : null;
        }

        if (childIndex + 1 < ChildNodes.Count)
        {
            var next = ChildNodes[childIndex + 1];
            next.PreviousSibling = childIndex > 0 ? ChildNodes[childIndex - 1] : null;
        }

        ChildNodes.RemoveAt(childIndex);
        child.Parent = null;
        child.PreviousSibling = null;
        child.NextSibling = null;

        return true;
    }

    protected void ArrangeSibling()
    {
        if (ChildNodes.Count == 0) return;

        var nodes = CollectionsMarshal.AsSpan(ChildNodes);
        nodes[0].PreviousSibling = null;

        for (var i = 1; i < nodes.Length; i++)
        {
            nodes[i - 1].NextSibling = nodes[i];
            nodes[i].PreviousSibling = nodes[i - 1];
        }

        nodes[^1].NextSibling = null;
    }

    public void MakeVirtual(T? parent)
    {
        IsVirtual = true;
        Parent = parent;
    }

    public int GetLastTick()
    {
        var maxTick = Tick.Original;
        foreach (var child in ChildNodes) maxTick = Math.Max(maxTick, child.GetLastTick());
        return maxTick;
    }

    protected void SortChild(Comparison<T> comparison)
    {
        ChildNodes.Sort(comparison);
        ArrangeSibling();
        foreach (var child in ChildNodes) child.SortChild(comparison);
    }

    public virtual void Sort()
    {
        SortChild((a, b) => a.Tick.CompareTo(b.Tick));
    }
}

public class Note : TimeNode<Note>
{
    public virtual int Lane { get; set; }
    public virtual int Width { get; set; } = 1;
    public int Timeline { get; set; }

    public bool IsViolate(Note other)
    {
        if (ReferenceEquals(this, other) || Tick.Original != other.Tick.Original || Timeline == other.Timeline)
            return false;

        static bool Contains(Note outer, Note inner) =>
            outer.Lane <= inner.Lane && outer.Lane + outer.Width >= inner.Lane + inner.Width;

        return (Timeline != 0 && Contains(this, other)) ||
               (other.Timeline != 0 && Contains(other, this));
    }

    public override void Sort()
    {
        // In CHUNITHM, notes appear to have layer priority where later ones render on top, covering those beneath.
        // However, in UMIGURI, AirCrash always renders on top regardless of order (but below than AirHold in NEXT).
        // To match UMIGURI's behavior, AirCrash notes are moved to the end of the collection.
        SortChild((x, y) =>
        {
            if (x is not AirCrash && y is not AirCrash) return CompareCommon();
            if (x is not AirCrash xCrash) return -1;
            if (y is not AirCrash yCrash) return 1;

            var result = CompareCommon();
            if (result != 0) return result;
            return xCrash.Color.CompareTo(yCrash.Color);

            int CompareCommon()
            {
                var i = x.Tick.CompareTo(y.Tick);
                if (i != 0) return i;
                i = x.Lane.CompareTo(y.Lane);
                if (i != 0) return i;
                i = x.Width.CompareTo(y.Width);
                if (i != 0) return i;
                return x.Timeline.CompareTo(y.Timeline);
            }
        });

        // move negative notes after the paired positive notes
        foreach (var child in Children.OfType<NegativeNote>().ToArray())
        {
            if (child.PairNote == null) continue;
            Note? pair = child.PairNote;
            while (pair != null && pair.Parent != this) pair = pair.Parent;
            if (pair == null || pair.Parent != this) continue;
            InsertBefore(pair, child);
        }
    }
}

public abstract class ExTapableNote : Note
{
    public abstract ExEffect? Effect { get; set; }
}

// you may get confused, but I just want to achieve type safety here
public abstract class PairableNote<TSelf, TPair> : Note where TSelf : PairableNote<TSelf, TPair>
    where TPair : PairableNote<TPair, TSelf>
{
    public TPair? PairNote { get; set; }

    public void MakePair(TPair? targetNote)
    {
        if ((targetNote?.IsVirtual ?? false) || IsVirtual)
            throw new InvalidOperationException("Cannot pair virtual notes");
        if (PairNote != null) PairNote.PairNote = null;
        if (targetNote == null)
        {
            PairNote = null;
        }
        else
        {
            if (targetNote.PairNote != null) targetNote.PairNote.PairNote = null;
            targetNote.PairNote = (TSelf)this;
            PairNote = targetNote;
        }
    }
}

// positive note: note that can have air 
public abstract class PositiveNote : PairableNote<PositiveNote, NegativeNote>;

// negative note: air note
public abstract class NegativeNote : PairableNote<NegativeNote, PositiveNote>;
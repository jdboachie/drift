using System.Collections.Generic;
using System.Linq;

namespace Drift;

/// <summary>
/// Brings a list shown on screen in line with what it should hold, by removing, moving and
/// inserting only what changed. Rows that stay keep their place (and their containers and
/// selection), so nothing re-animates or flickers.
/// </summary>
public static class ListSync
{
    public static void Apply(IList<object> list, IReadOnlyList<object> wanted)
    {
        var keep = new HashSet<object>(wanted);
        for (var i = list.Count - 1; i >= 0; i--)
            if (!keep.Contains(list[i])) list.RemoveAt(i);

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < list.Count && Equals(list[i], wanted[i])) continue;
            var at = -1;
            for (var j = i + 1; j < list.Count; j++)
                if (Equals(list[j], wanted[i])) { at = j; break; }
            if (at < 0) list.Insert(i, wanted[i]);
            else if (list is System.Collections.ObjectModel.ObservableCollection<object> observable) observable.Move(at, i);
            else
            {
                var item = list[at];
                list.RemoveAt(at);
                list.Insert(i, item);
            }
        }
        while (list.Count > wanted.Count) list.RemoveAt(list.Count - 1);
    }
}

using System.Text;

namespace MauiTabbedModalLeakRepro;

internal static class LeakTracker
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<object>> Tracked = new();
    private static int _trackedCount;

    public static void Clear()
    {
        lock (Gate)
        {
            Tracked.Clear();
            _trackedCount = 0;
        }

        ReproLog.Log("Cleared tracked WeakReferences.");
    }

    public static void Track(object obj, string tag)
    {
        if (obj == null)
        {
            return;
        }

        lock (Gate)
        {
            _trackedCount++;
            Tracked.Add(new WeakReference<object>(obj));
        }

        ReproLog.Log($"Track[{tag}] #{_trackedCount}; type={obj.GetType().FullName}");
    }

    public static void TrackTabbedPage(LeakyTabbedModalPage page)
    {
        Track(page, "TabbedPage");

        foreach (var child in page.Children)
        {
            Track(child, "TabChild");

            if (child is NavigationPage nav && nav.CurrentPage != null)
            {
                Track(nav.CurrentPage, "TabChildInner");
            }
        }
    }

    public static LeakSnapshot ForceGcAndGetSnapshot(string tag)
    {
        ForceFullGc();
        return GetSnapshot(tag);
    }

    // Obtain a snapshot without forcing a GC. Useful to compare before/after states.
    public static LeakSnapshot GetSnapshotWithoutGc(string tag)
    {
        return GetSnapshot(tag);
    }

    private static void ForceFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static LeakSnapshot GetSnapshot(string tag)
    {
        lock (Gate)
        {
            var alive = 0;
            var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var weakRef in Tracked)
            {
                if (!weakRef.TryGetTarget(out var obj))
                {
                    continue;
                }

                alive++;
                var key = obj.GetType().FullName ?? obj.GetType().Name;
                typeCounts[key] = typeCounts.TryGetValue(key, out var count) ? count + 1 : 1;
            }

            return new LeakSnapshot(tag, Tracked.Count, alive, typeCounts);
        }
    }

    internal sealed record LeakSnapshot(string Tag, int TotalTracked, int Alive, IReadOnlyDictionary<string, int> AliveTypes)
    {
        public string ToDisplayString()
        {
            var builder = new StringBuilder();
            builder.Append($"{Tag} | Alive={Alive}/{TotalTracked}");

            if (AliveTypes.Count > 0)
            {
                builder.Append(" | Types=");
                builder.Append(string.Join(", ", AliveTypes.OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}:{kv.Value}")));
            }

            return builder.ToString();
        }
    }
}

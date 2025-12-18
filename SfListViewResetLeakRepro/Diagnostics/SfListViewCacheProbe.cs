using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Collections;
using System.Linq;
using System.Diagnostics;
using System;
using Syncfusion.Maui.ListView;

namespace SfListViewResetLeakRepro.Diagnostics;

public static class SfListViewCacheProbe
{
    public static string GetTemplateViewCacheSummary(SfListView listView)
    {
        if (listView == null)
        {
            return "SfListView: <null>";
        }

        try
        {
            var sb = new StringBuilder();

            var itemsGenerator = TryGetItemsGenerator(listView);
            if (itemsGenerator == null)
            {
                sb.AppendLine("ItemsGenerator: <not found>");
                sb.AppendLine("TemplateViewCache: <not found>");
                return sb.ToString().TrimEnd();
            }

            sb.AppendLine($"ItemsGenerator: {itemsGenerator.GetType().FullName}");

            var caches = FindTemplateViewCaches(itemsGenerator);
            if (caches.Count == 0)
            {
                // Fallback: sometimes cache sits on the list itself or nested helper.
                caches.AddRange(FindTemplateViewCaches(listView));
            }

            if (caches.Count == 0)
            {
                sb.AppendLine("TemplateViewCache: <not found>");
                return sb.ToString().TrimEnd();
            }

            foreach (var line in caches)
            {
                sb.AppendLine(line);
            }

            var instances = FindTemplateViewCacheInstances(itemsGenerator, maxInstances: 50);
            if (instances.Count == 0)
            {
                instances = FindTemplateViewCacheInstances(listView, maxInstances: 50);
            }

            if (instances.Count > 0)
            {
                // De-dupe: we may discover the same underlying list via both a property and its backing field.
                var uniqueInstances = new HashSet<object>(ReferenceEqualityComparer.Instance);
                foreach (var bucket in instances)
                {
                    foreach (var inst in bucket.Instances)
                    {
                        uniqueInstances.Add(inst);
                    }
                }

                sb.AppendLine();
                sb.AppendLine($"TemplateViewCache instances (sampled): {uniqueInstances.Count}");

                foreach (var bucket in instances.OrderByDescending(b => b.Instances.Count))
                {
                    var bucketUnique = DedupPreserveOrder(bucket.Instances);

                    sb.AppendLine($"{bucket.Path}: {bucket.ContainerType} (count={bucketUnique.Count})");

                    var take = Math.Min(bucketUnique.Count, 12);
                    for (var i = 0; i < take; i++)
                    {
                        var inst = bucketUnique[i];
                        sb.AppendLine($"  [{i}] {SummarizeTemplateViewCache(inst)}");
                    }

                    if (bucketUnique.Count > take)
                    {
                        sb.AppendLine($"  ... ({bucketUnique.Count - take} more)");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"TemplateViewCache probe failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public static void LogTemplateViewCacheSummary(Syncfusion.Maui.ListView.SfListView listView, string tag = "SfListViewProbe")
    {
        var summary = GetTemplateViewCacheSummary(listView);

        // Write each non-empty line to multiple outputs so logcat / debug consoles pick it up.
        foreach (var raw in summary.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                // Single primary output: Console (captured by logcat/stdout).
                Console.WriteLine(raw);
            }
            catch { }
        }
    }

    private static object? TryGetItemsGenerator(SfListView listView)
    {
        var t = listView.GetType();

        // Common names first.
        var candidates = new[]
        {
            "ItemsGenerator",
            "itemsGenerator",
            "_itemsGenerator",
            "itemsGeneratorView",
            "_itemsGeneratorView"
        };

        foreach (var name in candidates)
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
            {
                try
                {
                    var value = p.GetValue(listView);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                try
                {
                    var value = f.GetValue(listView);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        // Last resort: find any member whose type name contains "ItemsGenerator".
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (p.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (!TypeNameContains(p.PropertyType, "ItemsGenerator"))
            {
                continue;
            }

            try
            {
                var value = p.GetValue(listView);
                if (value != null)
                {
                    return value;
                }
            }
            catch
            {
                // ignore
            }
        }

        foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!TypeNameContains(f.FieldType, "ItemsGenerator"))
            {
                continue;
            }

            try
            {
                var value = f.GetValue(listView);
                if (value != null)
                {
                    return value;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static List<string> FindTemplateViewCaches(object root)
    {
        var findings = new List<string>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(root, "root", depth: 0, findings, visited);
        return findings;
    }

    private static void Visit(object? obj, string path, int depth, List<string> findings, HashSet<object> visited)
    {
        if (obj == null)
        {
            return;
        }

        if (depth > 8)
        {
            return;
        }

        if (!visited.Add(obj))
        {
            return;
        }

        var t = obj.GetType();

        // Direct hit: type itself contains TemplateViewCache.
        if (TypeNameContains(t, "TemplateViewCache"))
        {
            var cnt = TryGetCount(obj);
            var shortType = t.Name;
            findings.Add(cnt.HasValue
                ? $"{path}: {shortType} (count={cnt.Value})"
                : $"{path}: {shortType}");
        }

        foreach (var member in t.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (member is not FieldInfo && member is not PropertyInfo)
            {
                continue;
            }

            Type? memberType = null;
            Func<object?>? getValue = null;

            if (member is FieldInfo field)
            {
                memberType = field.FieldType;
                getValue = () => field.GetValue(obj);
            }
            else if (member is PropertyInfo prop && prop.GetIndexParameters().Length == 0)
            {
                memberType = prop.PropertyType;
                getValue = () => prop.GetValue(obj);
            }
            else
            {
                continue;
            }

            // Cheap filter to reduce reflection churn.
            if (!MightContainCache(member.Name, memberType))
            {
                continue;
            }

            object? value;
            try
            {
                value = getValue();
            }
            catch
            {
                continue;
            }

            if (value == null)
            {
                continue;
            }

            if (TypeNameContains(memberType, "TemplateViewCache") || member.Name.Contains("templateviewcache", StringComparison.OrdinalIgnoreCase))
            {
                var shortType = value.GetType().Name;
                var cnt = TryGetCount(value);
                findings.Add(cnt.HasValue
                    ? $"{path}.{member.Name}: {shortType} (count={cnt.Value})"
                    : $"{path}.{member.Name}: {shortType}");
                continue;
            }

            // If the member is a collection, enumerate to find TemplateViewCache instances inside.
            if (value is System.Collections.IEnumerable ie && !(value is string))
            {
                try
                {
                    long found = 0;
                    int seen = 0;
                    foreach (var it in ie)
                    {
                        seen++;
                        if (it == null)
                            continue;

                        if (TypeNameContains(it.GetType(), "TemplateViewCache"))
                        {
                            found++;
                        }

                        if (seen > 2000)
                            break;
                    }

                    if (found > 0)
                    {
                        findings.Add($"{path}.{member.Name}: {value.GetType().Name} (contains TemplateViewCache count={found})");
                        continue;
                    }
                }
                catch
                {
                    // ignore enumeration errors
                }
            }

            // Recurse into nested objects to find caches hanging off helpers.
            if (!IsLeafType(memberType))
            {
                Visit(value, $"{path}.{member.Name}", depth + 1, findings, visited);
            }
        }
    }

    private sealed class TemplateViewCacheBucket
    {
        public required string Path { get; init; }
        public required string ContainerType { get; init; }
        public required object Container { get; init; }
        public required List<object> Instances { get; init; }
    }

    private static List<TemplateViewCacheBucket> FindTemplateViewCacheInstances(object root, int maxInstances)
    {
        var buckets = new List<TemplateViewCacheBucket>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectInstances(root, "root", depth: 0, buckets, visited, maxInstances);
        return buckets;
    }

    private static void CollectInstances(
        object? obj,
        string path,
        int depth,
        List<TemplateViewCacheBucket> buckets,
        HashSet<object> visited,
        int remainingBudget)
    {
        if (obj == null || remainingBudget <= 0)
        {
            return;
        }

        if (depth > 8)
        {
            return;
        }

        if (!visited.Add(obj))
        {
            return;
        }

        var t = obj.GetType();

        foreach (var member in t.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (member is not FieldInfo && member is not PropertyInfo)
            {
                continue;
            }

            Type? memberType = null;
            Func<object?>? getValue = null;

            if (member is FieldInfo field)
            {
                memberType = field.FieldType;
                getValue = () => field.GetValue(obj);
            }
            else if (member is PropertyInfo prop && prop.GetIndexParameters().Length == 0)
            {
                memberType = prop.PropertyType;
                getValue = () => prop.GetValue(obj);
            }
            else
            {
                continue;
            }

            if (!MightContainCache(member.Name, memberType))
            {
                continue;
            }

            object? value;
            try
            {
                value = getValue();
            }
            catch
            {
                continue;
            }

            if (value == null)
            {
                continue;
            }

            // If it's an IEnumerable, see if it directly contains TemplateViewCache.
            if (value is System.Collections.IEnumerable ie && value is not string)
            {
                var instances = new List<object>();
                try
                {
                    foreach (var it in ie)
                    {
                        if (it == null)
                        {
                            continue;
                        }

                        if (TypeNameContains(it.GetType(), "TemplateViewCache"))
                        {
                            instances.Add(it);
                            remainingBudget--;
                            if (remainingBudget <= 0)
                            {
                                break;
                            }
                        }

                        if (instances.Count >= 2000)
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    // ignore enumeration errors
                }

                if (instances.Count > 0)
                {
                    // Merge if we've already discovered this same container instance via another member name.
                    var existing = buckets.FirstOrDefault(b => ReferenceEquals(b.Container, value));
                    if (existing != null)
                    {
                        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                        foreach (var inst in existing.Instances)
                        {
                            seen.Add(inst);
                        }

                        foreach (var inst in instances)
                        {
                            if (seen.Add(inst))
                            {
                                existing.Instances.Add(inst);
                            }
                        }
                    }
                    else
                    {
                        buckets.Add(new TemplateViewCacheBucket
                        {
                            Path = $"{path}.{member.Name}",
                            ContainerType = value.GetType().FullName ?? value.GetType().Name,
                            Container = value,
                            Instances = DedupPreserveOrder(instances)
                        });
                    }

                    if (remainingBudget <= 0)
                    {
                        return;
                    }

                    // Don't recurse into this collection once we've got the TemplateViewCache instances.
                    continue;
                }
            }

            // Recurse into nested objects.
            if (!IsLeafType(memberType))
            {
                CollectInstances(value, $"{path}.{member.Name}", depth + 1, buckets, visited, remainingBudget);
                if (remainingBudget <= 0)
                {
                    return;
                }
            }
        }
    }

    private static string SummarizeTemplateViewCache(object templateViewCache)
    {
        var t = templateViewCache.GetType();
        var sb = new StringBuilder();
        sb.Append(t.FullName ?? t.Name);
        sb.Append($"#{RuntimeHelpers.GetHashCode(templateViewCache)}");

        var parts = new List<string>();

        // Heuristic: grab a few telling members (keys/templates/views/ids/index).
        foreach (var m in t.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m is not FieldInfo && m is not PropertyInfo)
            {
                continue;
            }

            string name;
            Type memberType;
            Func<object?> getValue;

            if (m is FieldInfo fi)
            {
                name = fi.Name;
                memberType = fi.FieldType;
                getValue = () => fi.GetValue(templateViewCache);
            }
            else if (m is PropertyInfo pi && pi.GetIndexParameters().Length == 0)
            {
                name = pi.Name;
                memberType = pi.PropertyType;
                getValue = () => pi.GetValue(templateViewCache);
            }
            else
            {
                continue;
            }

            if (!IsInterestingCacheMember(name, memberType))
            {
                continue;
            }

            object? value;
            try
            {
                value = getValue();
            }
            catch
            {
                continue;
            }

            if (value == null)
            {
                continue;
            }

            parts.Add($"{name}={FormatShortValue(value)}");
            if (parts.Count >= 8)
            {
                break;
            }
        }

        if (parts.Count > 0)
        {
            sb.Append("  ");
            sb.Append(string.Join("; ", parts));
        }

        return sb.ToString();
    }

    private static bool IsInterestingCacheMember(string name, Type memberType)
    {
        // Key / id / index-ish fields and anything template/view-related.
        if (name.Contains("key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("id", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("index", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("template", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("view", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cell", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("type", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Also allow leaf primitive-ish values so we can print identifiers if present.
        return IsLeafType(memberType) || memberType.IsEnum;
    }

    private static string FormatShortValue(object value)
    {
        if (value is string s)
        {
            // Keep it short.
            return s.Length <= 60 ? $"\"{s}\"" : $"\"{s.Substring(0, 57)}...\"";
        }

        var t = value.GetType();
        if (IsLeafType(t) || t.IsEnum)
        {
            return value.ToString() ?? t.Name;
        }

        if (value is Type ty)
        {
            return ty.FullName ?? ty.Name;
        }

        // For complex objects, output type + a stable-ish identity.
        return $"{t.Name}#{RuntimeHelpers.GetHashCode(value)}";
    }

    private static List<object> DedupPreserveOrder(List<object> instances)
    {
        if (instances.Count <= 1)
        {
            return instances;
        }

        var unique = new List<object>(capacity: instances.Count);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var inst in instances)
        {
            if (seen.Add(inst))
            {
                unique.Add(inst);
            }
        }

        return unique;
    }

    private static bool MightContainCache(string memberName, Type memberType)
    {
        if (memberName.Contains("templateviewcache", StringComparison.OrdinalIgnoreCase) ||
            memberName.Contains("itemsGenerator", StringComparison.OrdinalIgnoreCase) ||
            memberName.Contains("generator", StringComparison.OrdinalIgnoreCase) ||
            memberName.Contains("cache", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TypeNameContains(memberType, "TemplateViewCache") || TypeNameContains(memberType, "ItemsGenerator"))
        {
            return true;
        }

        // If this is a generic type, check its generic arguments for TemplateViewCache
        if (memberType.IsGenericType)
        {
            foreach (var ga in memberType.GetGenericArguments())
            {
                if (TypeNameContains(ga, "TemplateViewCache"))
                    return true;
            }
        }

        // If it's an IEnumerable of T where T's name contains TemplateViewCache, we should inspect it.
        try
        {
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(memberType))
            {
                var gargs = memberType.IsGenericType ? memberType.GetGenericArguments() : Type.EmptyTypes;
                foreach (var ga in gargs)
                {
                    if (TypeNameContains(ga, "TemplateViewCache"))
                        return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        if (memberType.IsArray)
        {
            var elementType = memberType.GetElementType();
            if (elementType != null && TypeNameContains(elementType, "TemplateViewCache"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLeafType(Type t)
    {
        if (t.IsPrimitive || t.IsEnum)
        {
            return true;
        }

        return t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(TimeSpan);
    }

    private static long? TryGetCount(object? value)
    {
        if (value == null)
            return null;

        // Arrays
        if (value is Array a)
            return a.LongLength;

        // Common non-generic collection
        if (value is ICollection col)
            return col.Count;

        var t = value.GetType();

        // Look for a Count or Length property
        var pi = t.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (pi != null && (pi.PropertyType == typeof(int) || pi.PropertyType == typeof(long)))
        {
            try
            {
                var v = pi.GetValue(value);
                if (v != null)
                    return Convert.ToInt64(v);
            }
            catch { }
        }

        var pli = t.GetProperty("Length", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (pli != null && (pli.PropertyType == typeof(int) || pli.PropertyType == typeof(long)))
        {
            try
            {
                var v = pli.GetValue(value);
                if (v != null)
                    return Convert.ToInt64(v);
            }
            catch { }
        }

        // Fallback: enumerate (best-effort, may be costly)
        if (value is IEnumerable ie)
        {
            try
            {
                long c = 0;
                var en = ie.GetEnumerator();
                while (en.MoveNext())
                    c++;
                return c;
            }
            catch { }
        }

        return null;
    }

    private static bool TypeNameContains(Type? t, string needle)
        => t?.Name?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true ||
           t?.FullName?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

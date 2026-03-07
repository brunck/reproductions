using System.Reflection;

namespace SyncfusionPickerMemoryLeak;

/// <summary>
/// Reads ThemeElement.elements via reflection to expose the static list that causes the memory leak.
///
/// ThemeElement (Syncfusion.Maui.Themes) holds a static Object[] of all registered theme-aware
/// controls. Entries are added on construction but NEVER removed — not by DisconnectHandler(),
/// Dispose(), or any other cleanup method. This causes unbounded memory growth.
/// </summary>
public static class ThemeElementInspector
{
    private static Type? _themeElementType;
    private static FieldInfo? _elementsField;
    private static bool _initialized;

    /// <summary>Returns the current count of non-null entries in ThemeElement.elements.</summary>
    public static int GetElementsCount()
    {
        EnsureInitialized();
        if (_elementsField == null) return -1;

        var value = _elementsField.GetValue(null);
        if (value is object[] array)
            return array.Count(x => x != null);
        if (value is System.Collections.ICollection col)
            return col.Count;
        return -1;
    }

    /// <summary>Returns a count-by-type-name breakdown of ThemeElement.elements contents.</summary>
    public static Dictionary<string, int> GetElementsByType()
    {
        EnsureInitialized();
        if (_elementsField == null) return [];

        var value = _elementsField.GetValue(null);
        IEnumerable<object?> items = value switch
        {
            object[] arr => arr,
            System.Collections.IEnumerable e => e.Cast<object?>(),
            _ => []
        };

        return items
            .Where(o => o != null)
            .GroupBy(o => o!.GetType().Name)
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                _themeElementType = assembly.GetType("Syncfusion.Maui.Themes.ThemeElement");
                if (_themeElementType != null) break;
            }
            catch { }
        }

        if (_themeElementType == null) return;

        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        // Try known field names first
        foreach (var name in new[] { "elements", "Elements", "_elements", "elementList", "themeElements" })
        {
            _elementsField = _themeElementType.GetField(name, flags);
            if (_elementsField != null) return;
        }

        // Fallback: first static array or collection field
        foreach (var field in _themeElementType.GetFields(flags))
        {
            if (field.FieldType.IsArray ||
                (typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(string)))
            {
                _elementsField = field;
                return;
            }
        }
    }
}

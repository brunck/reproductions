namespace MauiTabbedModalLeakRepro;

internal static class ReproDiagnostics
{
    public static void LogNavigationState(Page page, string tag)
    {
        try
        {
            var nav = page.Navigation;
            var navStack = nav?.NavigationStack?.Select(p => p.GetType().Name).ToArray() ?? Array.Empty<string>();
            var modalStack = nav?.ModalStack?.Select(p => p.GetType().Name).ToArray() ?? Array.Empty<string>();

            ReproLog.Log($"NavState[{tag}] NavStack=[{string.Join(",", navStack)}]; ModalStack=[{string.Join(",", modalStack)}]");
        }
        catch (Exception ex)
        {
            ReproLog.Log($"NavState[{tag}] failed: {ex.GetType().Name}");
        }
    }
}

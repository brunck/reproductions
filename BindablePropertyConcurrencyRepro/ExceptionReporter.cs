using System.Text;

namespace BindablePropertyConcurrencyRepro;

/// <summary>
/// Captures unhandled exceptions so the stack survives the process being killed.
/// Everything is written to the platform log with the <see cref="Tag"/> prefix (so
/// <c>adb logcat</c> picks it up) and appended to a file that <see cref="MainPage"/>
/// renders on the next launch.
/// </summary>
public static class ExceptionReporter
{
	public const string Tag = "BPRACE";

	static readonly object FileLock = new();
	static bool _installed;

	public static string LogPath => Path.Combine(FileSystem.AppDataDirectory, "last-crash.txt");

	public static void Install()
	{
		if (_installed)
			return;
		_installed = true;

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			Report("AppDomain.UnhandledException", e.ExceptionObject as Exception);

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			Report("TaskScheduler.UnobservedTaskException", e.Exception);
			e.SetObserved();
		};

#if ANDROID
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
			Report("AndroidEnvironment.UnhandledExceptionRaiser", e.Exception);
#endif
	}

	/// <summary>Records an exception. Safe to call from any thread.</summary>
	public static void Report(string source, Exception? exception)
	{
		var text = Describe(source, exception);

		// Platform log first — this is the copy that survives a process kill even if
		// the file write itself is what gets interrupted.
		Console.WriteLine($"{Tag} {text}");
		System.Diagnostics.Debug.WriteLine($"{Tag} {text}");

		try
		{
			lock (FileLock)
			{
				File.AppendAllText(LogPath, text + Environment.NewLine);
			}
		}
		catch
		{
			// Nothing useful to do while unwinding a crash.
		}
	}

	public static string Describe(string source, Exception? exception)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"=== {source} ===");
		sb.AppendLine($"Thread: {Environment.CurrentManagedThreadId}");
		sb.AppendLine(exception?.ToString() ?? "(no exception object)");
		return sb.ToString();
	}

	public static string ReadPreviousReports()
	{
		try
		{
			lock (FileLock)
			{
				return File.Exists(LogPath) ? File.ReadAllText(LogPath) : string.Empty;
			}
		}
		catch (Exception ex)
		{
			return $"(could not read {LogPath}: {ex.Message})";
		}
	}

	public static void ClearPreviousReports()
	{
		try
		{
			lock (FileLock)
			{
				if (File.Exists(LogPath))
					File.Delete(LogPath);
			}
		}
		catch
		{
			// Ignored; clearing the log is a convenience, not a requirement.
		}
	}
}

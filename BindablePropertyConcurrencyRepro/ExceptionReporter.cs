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

	/// <summary>
	/// Raised for exceptions that reached one of the unhandled hooks — i.e. ones no scenario loop
	/// could catch. <see cref="MainPage"/> uses it to attribute the failure to the run in flight.
	/// </summary>
	public static event Action<Exception>? Unhandled;

	public static string LogPath => Path.Combine(FileSystem.AppDataDirectory, "last-crash.txt");

	public static void Install()
	{
		if (_installed)
			return;
		_installed = true;

		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			Report("AppDomain.UnhandledException", e.ExceptionObject as Exception);
			Notify(e.ExceptionObject as Exception);
		};

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			Report("TaskScheduler.UnobservedTaskException", e.Exception);
			Notify(e.Exception);
			e.SetObserved();
		};

#if ANDROID
		Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
		{
			Report("AndroidEnvironment.UnhandledExceptionRaiser", e.Exception);
			Notify(e.Exception);

			// Scenario B can throw on the UI thread inside the framework's own animation ticker
			// (AnimationManager.OnFire -> ... -> VisualElement.set_Opacity), which is outside any
			// app code and therefore uncatchable by the scenario. Left alone that terminates the
			// process, so pressing B a few times kills the app before it can be pressed again.
			//
			// The stack has already been captured above, which is the whole point of the run — so
			// swallow it and stay alive. Marking an unhandled UI-thread exception handled is only
			// defensible because this is a repro harness with nothing to corrupt: the element that
			// threw is discarded at the end of the run either way.
			e.Handled = true;
		};
#endif
	}

	static void Notify(Exception? exception)
	{
		if (exception is null)
			return;

		try
		{
			Unhandled?.Invoke(exception);
		}
		catch
		{
			// A listener must never turn a captured crash into a second one.
		}
	}

	/// <summary>Records an exception. Safe to call from any thread.</summary>
	public static void Report(string source, Exception? exception, int? threadId = null)
	{
		var text = Describe(source, exception, threadId);

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

	/// <summary>
	/// Renders a report. Pass <paramref name="threadId"/> when the caller is not the thread that
	/// threw — a scenario reports its failure back on the UI thread, so the ambient thread id
	/// would be misleading. The unhandled-exception hooks above do run on the throwing thread and
	/// can leave it null.
	/// </summary>
	public static string Describe(string source, Exception? exception, int? threadId = null)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"=== {source} ===");
		sb.AppendLine($"Threw on thread: {threadId ?? Environment.CurrentManagedThreadId}");
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

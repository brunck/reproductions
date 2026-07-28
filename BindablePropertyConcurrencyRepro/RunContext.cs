using System.Diagnostics;

namespace BindablePropertyConcurrencyRepro;

/// <summary>
/// Bounds one scenario run and records the *first* exception with the elapsed time at the moment
/// it was thrown.
///
/// Timing it at the throw site matters: if worker exceptions were left to propagate out of
/// <c>Task.WhenAll</c>, the run would report the duration of the whole run rather than the real
/// time-to-failure, because <c>WhenAll</c> does not complete until every loop has exited.
/// </summary>
public sealed class RunContext : IDisposable
{
	readonly CancellationTokenSource _cts;
	readonly Stopwatch _stopwatch = Stopwatch.StartNew();

	int _failed;

	public RunContext(TimeSpan duration) => _cts = new CancellationTokenSource(duration);

	public CancellationToken Token => _cts.Token;

	public TimeSpan Elapsed => _stopwatch.Elapsed;

	/// <summary>The first exception any loop in this run threw, or null if the run stayed clean.</summary>
	public Exception? Failure { get; private set; }

	/// <summary>Elapsed time at the moment <see cref="Failure"/> was thrown.</summary>
	public TimeSpan FailureAt { get; private set; }

	/// <summary>Runs a loop body on a background thread. The first failure ends the whole run.</summary>
	public Task Background(Action<CancellationToken> body) => Task.Run(() =>
	{
		try
		{
			body(Token);
		}
		catch (Exception ex)
		{
			Fail(ex);
		}
	}, CancellationToken.None);

	/// <summary>Runs an async loop on the calling (UI) thread. The first failure ends the whole run.</summary>
	public async Task Foreground(Func<CancellationToken, Task> body)
	{
		try
		{
			await body(Token);
		}
		catch (Exception ex)
		{
			Fail(ex);
		}
	}

	public void Cancel() => _cts.Cancel();

	void Fail(Exception exception)
	{
		if (Interlocked.Exchange(ref _failed, 1) == 0)
		{
			FailureAt = _stopwatch.Elapsed;
			Failure = exception;
		}

		Cancel();
	}

	public void Dispose() => _cts.Dispose();
}

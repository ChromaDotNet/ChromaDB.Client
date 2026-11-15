using System.Net;

namespace ChromaDB.Client.Backup.Common;

// The attempts of the copy command of the CLI of VecturaDB (rust/cli/src/commands/copy.rs, until_done and can_wait_for), for the
// hours that a backup of a large collection can take: the request goes again, without an end, while the error is one that waiting
// can fix: 429, 408, a 5xx but 501, a network error or a request with no answer in 120 s. Not a 4xx, a 501 or a quota of Chroma Cloud,
// which waiting does not change. The first wait is 0.5 s, doubled up to 60 s, or the Retry-After of the answer, from 0.5 to 60 s.
internal static class Waiting
{
	internal static TimeSpan FirstDelay = TimeSpan.FromMilliseconds(500);
	internal static TimeSpan MaxDelay = TimeSpan.FromSeconds(60);
	internal static TimeSpan AttemptTimeout = TimeSpan.FromSeconds(120);

	public static async Task<T> UntilDone<T>(Func<CancellationToken, Task<T>> call, Action<string>? waiting, CancellationToken cancellationToken)
	{
		var delay = FirstDelay;
		while (true)
		{
			using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			attempt.CancelAfter(AttemptTimeout);
			try
			{
				return await call(attempt.Token).ConfigureAwait(false);
			}
			catch (Exception ex) when (!cancellationToken.IsCancellationRequested && CanWaitFor(ex, attempt.IsCancellationRequested))
			{
				var wait = ex is ChromaException { RetryAfter: { } after }
					? TimeSpan.FromTicks(Math.Min(Math.Max(after.Ticks, FirstDelay.Ticks), MaxDelay.Ticks))
					: delay;
				waiting?.Invoke($"{Reason(ex, attempt.IsCancellationRequested)}: the request goes again in {wait.TotalSeconds:0.#} s");
				await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
				delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks));
			}
		}
	}

	public static Task UntilDone(Func<CancellationToken, Task> call, Action<string>? waiting, CancellationToken cancellationToken)
		=> UntilDone<bool>(async token => { await call(token).ConfigureAwait(false); return true; }, waiting, cancellationToken);

	internal static bool CanWaitFor(Exception ex, bool timedOut)
		=> ex switch
		{
			ChromaException { StatusCode: { } status } chroma => !chroma.Message.Contains("quota") && ((int)status is 429 or 408 || (int)status >= 500 && status != HttpStatusCode.NotImplemented),
			ChromaException { InnerException: HttpRequestException or IOException or OperationCanceledException } => true,
			HttpRequestException or IOException => true,
			OperationCanceledException => timedOut,
			_ => false,
		};

	private static string Reason(Exception ex, bool timedOut)
		=> ex is OperationCanceledException && timedOut ? $"No answer in {AttemptTimeout.TotalSeconds:0} s"
			: ex is ChromaException { StatusCode: { } status } ? $"The server answered {(int)status}"
			: $"No answer: {ex.Message}";
}

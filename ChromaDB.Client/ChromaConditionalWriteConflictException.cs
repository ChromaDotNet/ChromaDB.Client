namespace ChromaDB.Client;

/// <summary>
/// The commit of a conditional transaction that the server refused, because a record that the transaction read or writes changed after
/// its reads: the status code <c>409</c>. The server wrote none of its records. <c>RunConditionalTransactionAsync</c> runs the
/// transaction again.
/// </summary>
public class ChromaConditionalWriteConflictException : ChromaException
{
	/// <summary>
	/// An exception without a message.
	/// </summary>
	public ChromaConditionalWriteConflictException() { }
	/// <summary>
	/// An exception with the given message.
	/// </summary>
	/// <param name="message">The message.</param>
	public ChromaConditionalWriteConflictException(string? message) : base(message) { }
	/// <summary>
	/// An exception with the given message and the exception that caused it.
	/// </summary>
	/// <param name="message">The message.</param>
	/// <param name="inner">The exception that caused this one.</param>
	public ChromaConditionalWriteConflictException(string? message, Exception? inner) : base(message, inner) { }
}

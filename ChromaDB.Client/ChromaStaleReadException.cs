namespace ChromaDB.Client;

/// <summary>
/// A read or the commit of a conditional transaction that the server refused, because records changed after the first read of the
/// transaction: the status code <c>412</c>. <c>RunConditionalTransactionAsync</c> runs the transaction again.
/// </summary>
public class ChromaStaleReadException : ChromaException
{
	/// <summary>
	/// An exception without a message.
	/// </summary>
	public ChromaStaleReadException() { }
	/// <summary>
	/// An exception with the given message.
	/// </summary>
	/// <param name="message">The message.</param>
	public ChromaStaleReadException(string? message) : base(message) { }
	/// <summary>
	/// An exception with the given message and the exception that caused it.
	/// </summary>
	/// <param name="message">The message.</param>
	/// <param name="inner">The exception that caused this one.</param>
	public ChromaStaleReadException(string? message, Exception? inner) : base(message, inner) { }
}

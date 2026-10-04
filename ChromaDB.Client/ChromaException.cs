using System.Net;

namespace ChromaDB.Client;

/// <summary>
/// The error of a failed request, with the status code and the kind of error from the answer of the server, when there is one.
/// </summary>
public class ChromaException : Exception
{
	/// <summary>
	/// An exception without a message.
	/// </summary>
	public ChromaException() { }
	/// <summary>
	/// An exception with the given message.
	/// </summary>
	public ChromaException(string? message) : base(message) { }
	/// <summary>
	/// An exception with the given message and the exception that caused it.
	/// </summary>
	public ChromaException(string? message, Exception? inner) : base(message, inner) { }

	/// <summary>
	/// The status code of the answer of the server; null when there was no answer, like on a timeout, or when the client found the error.
	/// </summary>
	public HttpStatusCode? StatusCode { get; init; }

	/// <summary>
	/// The kind of error that the server names, like <c>NotFoundError</c> or <c>InvalidArgumentError</c> from Chroma 1.x,
	/// <c>InvalidCollection</c> from Chroma 0.5 and 0.6, <c>ValueError</c> from the v1 API of Chroma 0.4; null when it names none.
	/// </summary>
	public string? ErrorType { get; init; }
}
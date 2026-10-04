using System.Net;

namespace ChromaDB.Client;

public class ChromaException : Exception
{
	public ChromaException() { }
	public ChromaException(string? message) : base(message) { }
	public ChromaException(string? message, Exception? inner) : base(message, inner) { }

	// The status code of the answer of the server; null when there was no answer, like on a timeout, or when the client found the error.
	public HttpStatusCode? StatusCode { get; init; }
}
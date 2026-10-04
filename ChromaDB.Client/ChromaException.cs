using System.Net;

namespace ChromaDB.Client;

public class ChromaException : Exception
{
	public ChromaException() { }
	public ChromaException(string? message) : base(message) { }
	public ChromaException(string? message, Exception? inner) : base(message, inner) { }

	// The status code of the answer of the server; null when there was no answer, like on a timeout, or when the client found the error.
	public HttpStatusCode? StatusCode { get; init; }

	// The kind of error that the server names, like NotFoundError or InvalidArgumentError from Chroma 1.x, InvalidCollection
	// from Chroma 0.5 and 0.6, ValueError from the v1 API of Chroma 0.4; null when it names none.
	public string? ErrorType { get; init; }
}
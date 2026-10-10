# Errors

A failed request throws a `ChromaException`, with the original exception as `InnerException`: an error answer from the server, a network error, an answer that is not the expected JSON, or a timeout. Other exceptions, like an assembly that does not load or a disposed `HttpClient`, are not wrapped.

- `StatusCode` is the HTTP status of the answer, or null when there was no answer, like on a timeout.
- `ErrorType` is the kind of error the server names: `NotFoundError` or `InvalidArgumentError` from Chroma 1.x, `InvalidCollection` from Chroma 0.5 and 0.6, `ValueError` from the v1 API of Chroma 0.4, or null when it names none.

```csharp
if (!await client.CollectionExistsAsync("my_collection"))
{
	await client.CreateCollectionAsync("my_collection");
}
```

When a 0.x server rejects a request with validation errors, the message lists them, like `body.n_results: Input should be a valid integer`.

`CollectionExistsAsync` tells a missing collection apart from other errors on every tested server (KD-43 in [docs/COMPATIBILITY.md](COMPATIBILITY.md)). Any other error, like a bare `404` from a wrong address, throws.

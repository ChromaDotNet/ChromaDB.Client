using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The vectors of the BM25 function of the Python client of Chroma 1.5.9 (ChromaBm25EmbeddingFunction, with snowballstemmer 3.1.1)
// for the same texts: the indices are equal, and the values are the float of the double that Python computes.
[TestFixture]
public class ChromaBm25Tests
{
	[TestCase("The quick brown fox jumps over the lazy dog.", new uint[] { 226376294, 741580288, 771291085, 1312749093, 1621867415, 1913189942 }, new double[] { 1.6652868125369606, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606 })]
	[TestCase("Running runners ran; they're RUNNING again!!! Generously, generalizations.", new uint[] { 243905464, 567658162, 946033505, 1026658409, 1068447005 }, new double[] { 1.8956580276001347, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606 })]
	[TestCase("the and of a", new uint[0], new double[0])]
	[TestCase("", new uint[0], new double[0])]
	[TestCase("repeated repeated repeated word word unique", new uint[] { 209724451, 641155872, 968174432 }, new double[] { 1.6652868125369606, 1.9872971065631617, 1.8956580276001347 })]
	[TestCase("Hello, world \u2014 it's a beautiful day... e-mail foo_bar 3.14", new uint[] { 74040069, 116628897, 264741300, 520205939, 613153351, 1572539931, 1701593959, 1749031367, 2006997753 }, new double[] { 1.6520973892637139, 1.6520973892637139, 1.6520973892637139, 1.6520973892637139, 1.6520973892637139, 1.6520973892637139, 1.6520973892637139, 1.6520973892637139, 1.6520973892637139 })]
	[TestCase("\u039F\u0394\u039F\u03A3 \u03A3\u039F\u03A6\u039F\u03A3 \u03BB\u03CC\u03B3\u03BF\u03C2", new uint[] { 17478605, 214956135, 1332225548 }, new double[] { 1.6786885245901642, 1.6786885245901642, 1.6786885245901642 })]
	[TestCase("\u0130stanbul \u015E\u0130MD\u0130 caf\u00E9 na\u00EFve", new uint[] { 605818632, 756638508, 1467803446, 1524208432 }, new double[] { 1.6741973840665876, 1.6741973840665876, 1.6741973840665876, 1.6741973840665876 })]
	[TestCase("emoji \U0001F600 test \U0001F680\U0001F680 rocket", new uint[] { 430021617, 515862427, 1167338989 }, new double[] { 1.6786885245901642, 1.6786885245901642, 1.6786885245901642 })]
	[TestCase("supercalifragilisticexpialidocious xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", new uint[] { 158684264 }, new double[] { 1.6877434821696136 })]
	[TestCase("tab\u0009separated\u000Anew line \u001Cfile\u001Dgroup", new uint[] { 422208903, 437367475, 515475255, 640477688, 892506137, 1021187622 }, new double[] { 1.6652868125369606, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606, 1.6652868125369606 })]
	[TestCase("1872d942 apple", new uint[] { 1085451005, 2147483648 }, new double[] { 1.6832038254632398, 1.6832038254632398 })]
	public void SameVectorsAsPython(string text, uint[] indices, double[] values)
	{
		var vector = new ChromaBm25().Embed(text);
		Assert.That(vector.Indices, Is.EqualTo(indices));
		Assert.That(vector.Values, Is.EqualTo(values.Select(x => (float)x)));
		Assert.That(vector.Tokens, Is.Null);
	}

	[Test]
	public void SameVectorsAsPythonWithOtherSettings()
	{
		var bm25 = new ChromaBm25(k: 1.5, b: 0.6, avgDocLength: 100, tokenMaxLength: 10, stopwords: ["quick", "fox"], includeTokens: true);
		var vector = bm25.Embed("The quick brown fox jumps over the lazy dog. Foxes!");
		Assert.That(vector.Indices, Is.EqualTo(new uint[] { 226376294, 741580288, 1132748958, 1312749093, 1621867415, 1656251611, 1913189942 }));
		Assert.That(vector.Values, Is.EqualTo(new double[] { 1.4952153110047846, 1.4952153110047846, 1.87125748502994, 1.4952153110047846, 1.4952153110047846, 1.4952153110047846, 1.4952153110047846 }.Select(x => (float)x)));
		Assert.That(vector.Tokens, Is.EqualTo(new[] { "lazi", "brown", "the", "dog", "fox", "over", "jump" }));
	}

	// Python lowercases both the stopword and the text to "i\u0307\u03C2": the final sigma skips the combining dot, which is not a word character.
	[Test]
	public void StopwordsLowercasedAsPython()
	{
		Assert.That(new ChromaBm25(stopwords: ["i\u0307\u03A3"]).Embed("\u0130\u03A3").Indices, Is.Empty);
	}

	// The configuration of chroma_bm25 as the Python client writes it in a schema: the stopwords only when they are not the default ones.
	[Test]
	public async Task Reference()
	{
		Assert.That(await Json(new ChromaBm25().Reference), Is.EqualTo("""{"type":"known","name":"chroma_bm25","config":{"k":1.2,"b":0.75,"avg_doc_length":256,"token_max_length":40,"include_tokens":false}}"""));
		Assert.That(await Json(new ChromaBm25(k: 1.5, stopwords: ["quick", "fox"], includeTokens: true).Reference),
			Is.EqualTo("""{"type":"known","name":"chroma_bm25","config":{"k":1.5,"b":0.75,"avg_doc_length":256,"token_max_length":40,"include_tokens":true,"stopwords":["quick","fox"]}}"""));
	}

	[Test]
	public void EmbedMany()
	{
		var bm25 = new ChromaBm25();
		var vectors = bm25.Embed(["apple pie", "", "banana split"]);
		Assert.That(vectors.Select(x => x.Indices.Count), Is.EqualTo(new[] { 2, 0, 2 }));
		Assert.That(vectors[0].Indices, Is.EqualTo(bm25.Embed("apple pie").Indices));
	}

	// The schema keeps the reference as JSON: the request of a collection created with it.
	static async Task<string> Json(ChromaEmbeddingFunctionReference reference)
	{
		var handler = new RecordingHandler();
		var client = new ChromaClient(new ChromaConfigurationOptions("http://localhost:8000"), new HttpClient(handler));
		await client.CreateCollection(new ChromaCollectionDefinition("c") { Schema = new ChromaCollectionSchema().WithSparseVectorIndex("v", "#document", bm25: true, reference) });
		using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
		return body.RootElement.GetProperty("schema").GetProperty("keys").GetProperty("v").GetProperty("sparse_vector").GetProperty("sparse_vector_index")
			.GetProperty("config").GetProperty("embedding_function").GetRawText();
	}

	sealed class RecordingHandler : HttpMessageHandler
	{
		public string? Body { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Body = await request.Content!.ReadAsStringAsync(cancellationToken);
			return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent("""{"id":"11111111-2222-3333-4444-555555555555","name":"c","schema":{"defaults":{},"keys":{}}}"""),
			};
		}
	}
}

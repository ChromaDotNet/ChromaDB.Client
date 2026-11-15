using ChromaDB.Client.Models;
using ChromaDB.Tool;
using NUnit.Framework;

namespace ChromaDB.Client.Tests;

// The command chromadotnet against the server of the tests: backup, restore and verify, their exit codes, and a restore without a
// terminal to ask in.
[TestFixture]
public class ToolTests : ChromaTestsBase
{
	string _path = null!;

	[SetUp]
	public void SetUp()
	{
		if (!BackupTested) { Assert.Ignore("The backup is tested with Chroma 1.5.9 and later, VecturaDB and Chroma Cloud."); }
		_path = Path.Combine(Path.GetTempPath(), $"chromadotnet-tool-test-{Guid.NewGuid():N}.zip");
	}

	[TearDown]
	public void TearDown() { if (_path is not null) { File.Delete(_path); } }

	[Test]
	public async Task BackupRestoreAndVerify()
	{
		var (client, name) = await Collection();
		var backup = await Run(null, "backup", "--collections", name, "--out", _path);
		Assert.That(backup.Code, Is.EqualTo(0), backup.Error);
		Assert.That(backup.Output, Does.Contain($"{name}: 3 records"));
		await client.DeleteCollectionAsync(name);

		var restore = await Run(null, "restore", "--in", _path);
		Assert.That(restore.Code, Is.EqualTo(0), restore.Error);
		Assert.That(restore.Output, Does.Contain($"{name}: created, 3 records added"));
		var verify = await Run(null, "verify", "--in", _path);
		Assert.That(verify.Code, Is.EqualTo(0), verify.Error);
		Assert.That(verify.Output, Does.Contain($"{name}: the same, 3 records"));
	}

	// Without a terminal and without --yes, a collection that is not the same stays as it is, and the command exits with 1; with
	// --force --yes it becomes the same.
	[Test]
	public async Task ACollectionThatIsNotTheSame()
	{
		var (client, name) = await Collection();
		await Run(null, "backup", "--collections", name, "--out", _path);
		await client.GetCollectionClient(name).DeleteAsync(["a"]);

		var verify = await Run(null, "verify", "--in", _path);
		Assert.That(verify.Code, Is.EqualTo(2));
		Assert.That(verify.Output, Does.Contain("1 missing"));

		var left = await Run(null, "restore", "--in", _path);
		Assert.That(left.Code, Is.EqualTo(1));
		Assert.That(left.Error, Does.Contain("--resume or --force").And.Contain("--yes"));
		Assert.That(await client.GetCollectionClient(name).CountAsync(), Is.EqualTo(2));

		var forced = await Run(null, "restore", "--in", _path, "--force", "--yes");
		Assert.That(forced.Code, Is.EqualTo(0), forced.Error);
		Assert.That((await Run(null, "verify", "--in", _path)).Code, Is.EqualTo(0));
	}

	// With a terminal, the question: r adds the missing records.
	[Test]
	public async Task TheQuestionWithATerminal()
	{
		var (client, name) = await Collection();
		await Run(null, "backup", "--collections", name, "--out", _path);
		await client.GetCollectionClient(name).DeleteAsync(["b"]);

		var resumed = await Run(new StringReader("r\n"), "restore", "--in", _path);
		Assert.That(resumed.Code, Is.EqualTo(0), resumed.Error);
		Assert.That(resumed.Output, Does.Contain("[r]esume").And.Contain($"{name}: 1 records added"));
	}

	[Test]
	public async Task ABackupNamesItsCollectionsOrSavesAll()
	{
		var result = await Run(null, "backup", "--out", _path);
		Assert.That(result.Code, Is.Not.EqualTo(0));
		Assert.That(result.Error, Does.Contain("--all"));
		Assert.That(File.Exists(_path), Is.False);
	}

	async Task<(ChromaClient, string)> Collection()
	{
		var client = new ChromaClient(BaseConfigurationOptions, HttpClient);
		var name = $"collection{Random.Shared.Next()}";
		await client.CreateCollectionAsync(name);
		await client.GetCollectionClient(name).AddAsync(new ChromaRecords(["a", "b", "c"])
		{
			Embeddings = [new([1f, 0f]), new([0f, 1f]), new([0.5f, 0.5f])],
			Metadatas = [new Dictionary<string, object> { ["i"] = 1L }, null, new Dictionary<string, object> { ["f"] = 0.5 }],
			Documents = ["one", null, "three"],
		});
		return (client, name);
	}

	// The command with the address, the token, the tenant and the database of the server of the tests, and its HttpClient.
	async Task<(int Code, string Output, string Error)> Run(TextReader? input, params string[] args)
	{
		var options = BaseConfigurationOptions;
		var all = new List<string>(args) { "--url", options.Uri.ToString() };
		if (options.ChromaToken is { } token)
		{
			all.AddRange(["--token", token]);
		}
		if (options.Tenant is { } tenant)
		{
			all.AddRange(["--tenant", tenant]);
		}
		if (options.Database is { } database)
		{
			all.AddRange(["--database", database]);
		}
		if (DistributedChroma)
		{
			all.Add("--distributed");
		}
		var output = new StringWriter();
		var error = new StringWriter();
		var code = await Program.RunAsync([.. all], output, error, input, settings => new ChromaClient(settings, HttpClient));
		return (code, output.ToString(), error.ToString());
	}
}

// The warning for a server that the tests do not try, against a fake one: the command goes on.
[TestFixture]
public class ToolWarningTests
{
	[Test]
	public async Task AServerNotTestedGetsAWarning()
	{
		var path = Path.Combine(Path.GetTempPath(), $"chromadotnet-tool-test-{Guid.NewGuid():N}.zip");
		try
		{
			var server = new BackupFakeServerTests.FakeChroma(3) { Version = "1.5.8" };
			var output = new StringWriter();
			var error = new StringWriter();
			var code = await Program.RunAsync(["backup", "--url", "http://localhost:8000", "--all", "--out", path], output, error, null, settings => new ChromaClient(settings, new HttpClient(server)));
			Assert.That(code, Is.EqualTo(0), error.ToString());
			Assert.That(error.ToString(), Does.Contain("Chroma 1.5.8 has not been tested"));
			Assert.That(File.Exists(path), Is.True);
		}
		finally
		{
			File.Delete(path);
		}
	}

	// Distributed Chroma installed by yourself is not tried by the tests, which try Chroma Cloud.
	[Test]
	public async Task DistributedChromaInstalledByYourselfGetsAWarning()
	{
		var path = Path.Combine(Path.GetTempPath(), $"chromadotnet-tool-test-{Guid.NewGuid():N}.zip");
		try
		{
			var server = new BackupFakeServerTests.FakeChroma(3);
			var error = new StringWriter();
			var code = await Program.RunAsync(["backup", "--url", "http://localhost:8000", "--distributed", "--all", "--out", path], new StringWriter(), error, null, settings => new ChromaClient(settings, new HttpClient(server)));
			Assert.That(code, Is.EqualTo(0), error.ToString());
			Assert.That(error.ToString(), Does.Contain("Distributed Chroma installed by yourself has not been tested"));
		}
		finally
		{
			File.Delete(path);
		}
	}
}

using System.Security.Cryptography;

namespace ChromaDB.Client.Backup.Common;

// The SHA-256 of bytes that come in parts. IncrementalHash is not in .NET Framework 4.6.2; TransformBlock is on every target.
internal sealed class Sha256 : IDisposable
{
	private readonly SHA256 _algorithm = SHA256.Create();

	public void Append(byte[] buffer, int offset, int count) => _algorithm.TransformBlock(buffer, offset, count, null, 0);

	public string Hex()
	{
		_algorithm.TransformFinalBlock([], 0, 0);
		return BackupFile.Hex(_algorithm.Hash!);
	}

	public void Dispose() => _algorithm.Dispose();
}

using System.Text;

namespace ChromaDB.Client.Common;

// MurmurHash3 x86_32 of the UTF-8 bytes of a string, as mmh3.hash(text, seed) of Python returns it: a signed 32-bit integer.
internal static class MurmurHash3
{
	public static int Hash32(string text, uint seed = 0)
	{
		var data = Encoding.UTF8.GetBytes(text);
		const uint c1 = 0xcc9e2d51;
		const uint c2 = 0x1b873593;
		var h1 = seed;
		var blocks = data.Length / 4;
		for (var i = 0; i < blocks; i++)
		{
			var k1 = (uint)(data[i * 4] | data[i * 4 + 1] << 8 | data[i * 4 + 2] << 16 | data[i * 4 + 3] << 24);
			k1 *= c1;
			k1 = RotateLeft(k1, 15);
			k1 *= c2;
			h1 ^= k1;
			h1 = RotateLeft(h1, 13);
			h1 = h1 * 5 + 0xe6546b64;
		}
		var tail = blocks * 4;
		uint k = 0;
		switch (data.Length & 3)
		{
			case 3:
				k ^= (uint)data[tail + 2] << 16;
				goto case 2;
			case 2:
				k ^= (uint)data[tail + 1] << 8;
				goto case 1;
			case 1:
				k ^= data[tail];
				k *= c1;
				k = RotateLeft(k, 15);
				k *= c2;
				h1 ^= k;
				break;
		}
		h1 ^= (uint)data.Length;
		h1 ^= h1 >> 16;
		h1 *= 0x85ebca6b;
		h1 ^= h1 >> 13;
		h1 *= 0xc2b2ae35;
		h1 ^= h1 >> 16;
		return unchecked((int)h1);
	}

	static uint RotateLeft(uint x, int r)
		=> x << r | x >> (32 - r);
}

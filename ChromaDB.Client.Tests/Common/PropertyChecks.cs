using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using Microsoft.FSharp.Core;

namespace ChromaDB.Client.Tests.Common;

// Checks a property with FsCheck on random cases, new ones on each run. A failure prints the case and its seed, like
// "(10774104525617377468,14267461796616028217)", and the seed of the failing step, with its size: the environment variable
// FSCHECK_REPLAY with either runs the same cases again. The CI sets one, so that its runs check the same cases.
internal static class PropertyChecks
{
	public static void ForAll<T>(Gen<T> cases, Func<T, Property> property, int count)
		=> Check.One(Config.QuickThrowOnFailure.WithMaxTest(count).WithReplay(Replay()), Prop.ForAll(cases.ToArbitrary(), property));

	public static void ForAll<T>(Gen<T> cases, Func<T, bool> property, int count)
		=> ForAll(cases, x => property(x).ToProperty(), count);

	static FSharpOption<Replay>? Replay()
	{
		if (Environment.GetEnvironmentVariable("FSCHECK_REPLAY") is not { Length: > 0 } replay)
		{
			return FSharpOption<Replay>.None;
		}
		var numbers = replay.Trim('(', ')', ' ').Split(',')
			.Select(x => ulong.TryParse(x.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : (ulong?)null).ToList();
		if (numbers.Count is not (2 or 3) || numbers.Contains(null) || numbers.Count == 3 && numbers[2] > int.MaxValue)
		{
			throw new ArgumentException($"FSCHECK_REPLAY is \"{replay}\": it takes a seed as FsCheck prints it, like \"(10774104525617377468,14267461796616028217)\", or with the size, like \"(10972319524849194178,79312946374890781,2)\".");
		}
		var size = numbers.Count == 3 ? FSharpOption<int>.Some((int)numbers[2]!.Value) : FSharpOption<int>.None;
		return FSharpOption<Replay>.Some(new Replay(new Rnd(numbers[0]!.Value, numbers[1]!.Value), size));
	}
}

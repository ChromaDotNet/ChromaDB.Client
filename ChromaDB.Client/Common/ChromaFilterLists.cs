namespace ChromaDB.Client.Common;

// The lists of $and and $or as they go to the server. A single Chroma server turns a filter into an SQLite expression where a
// list of n filters is n deep, plus its deepest filter, and SQLite stops at 1000: Chroma 1.5.9 takes 987 to 994 filters in
// one list, whatever the operator, and from about 4,400 it crashes on a stack overflow; where and where_document have a limit each.
// The client counts that depth: up to 900, with room for the rest of the query, a filter goes as it is, one list for a chain, as
// the Python client writes it. Beyond, each list of n filters goes as ⌈√n⌉ lists of about √n, with the same meaning, about 2√n deep.
internal static class ChromaFilterLists
{
	public const int MaxDepth = 900;

	public static Dictionary<string, object> Shape(Dictionary<string, object> filter)
		=> Depth(filter) <= MaxDepth ? filter : (Dictionary<string, object>)Split(filter);

	// At most the depth of the SQLite expression: the deepest filter of a list counts as if it were the first of the chain.
	private static int Depth(object filter)
		=> List(filter) is var (_, filters) ? filters.Length - 1 + filters.Max(Depth) : 1;

	private static object Split(object filter)
	{
		if (List(filter) is not var (@operator, filters))
		{
			return filter;
		}
		var operands = filters.Select(Split).ToList();
		var count = (int)Math.Ceiling(Math.Sqrt(operands.Count));
		// Fewer than 5 filters are as deep in one list as in groups.
		if (operands.Count < 5)
		{
			return new Dictionary<string, object> { [@operator] = operands.ToArray() };
		}
		var groups = new List<object>();
		for (int i = 0, start = 0; i < count; i++)
		{
			// Sizes that differ by one at most; Chroma takes $and and $or with two filters or more, so a group of one is the filter.
			var size = operands.Count / count + (i < operands.Count % count ? 1 : 0);
			groups.Add(size == 1 ? operands[start] : new Dictionary<string, object> { [@operator] = operands.GetRange(start, size).ToArray() });
			start += size;
		}
		return new Dictionary<string, object> { [@operator] = groups.ToArray() };
	}

	private static (string Operator, object[] Filters)? List(object filter)
		=> filter is Dictionary<string, object> { Count: 1 } dictionary && dictionary.First() is { Key: "$and" or "$or" } pair && pair.Value is object[] filters
			? (pair.Key, filters)
			: null;
}

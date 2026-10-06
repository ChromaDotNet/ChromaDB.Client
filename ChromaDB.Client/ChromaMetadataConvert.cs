using System.Collections;
using System.Globalization;

namespace ChromaDB.Client;

/// <summary>
/// Converts .NET values to metadata values and back, always in the same form, so that a value is read back as it was written and a
/// filter with the converted value finds it. Chroma metadata holds strings, integers, floating-point numbers, Booleans, and lists of
/// them from Chroma 1.5.0: dates go as text. The client does not convert the values of a metadata dictionary by itself.
/// </summary>
public static class ChromaMetadataConvert
{
	/// <summary>
	/// The metadata value of a .NET value:
	/// <list type="bullet">
	/// <item>a string, a <c>bool</c>, an <c>int</c>, a <c>long</c>, a <c>float</c> or a <c>double</c> as it is;</item>
	/// <item>a <c>DateTimeOffset</c> as round-trip text (<c>"O"</c>) in UTC, so that equal instants are equal text, as <c>==</c> compares them;</item>
	/// <item>a <c>DateTime</c> as round-trip text with its <c>Kind</c>, which it reads back with;</item>
	/// <item>a <c>DateOnly</c> as <c>yyyy-MM-dd</c>;</item>
	/// <item>a sequence of one of these as a list;</item>
	/// <item>null, and an empty sequence, as null: Chroma stores neither, so the key has no value.</item>
	/// </list>
	/// </summary>
	/// <param name="value">The .NET value.</param>
	/// <returns>The metadata value, or null for no value.</returns>
	/// <exception cref="ArgumentException">The value has another type, or a sequence holds a null, a value of another type, or values of
	/// more than one of the types of Chroma: strings, integers, floating-point numbers and Booleans.</exception>
	public static object? ToMetadataValue(object? value)
		=> value switch
		{
			null => null,
			string => value,
			IEnumerable values => ToList(values),
			_ => ToScalar(value),
		};

	/// <summary>
	/// The metadata of a record from .NET values, each converted with <c>ToMetadataValue</c>. A value that converts to null, as null and
	/// an empty sequence do, stays in the metadata as null: in <c>UpdateAsync</c> and <c>UpsertAsync</c> it deletes the key.
	/// </summary>
	/// <param name="values">The keys and the .NET values.</param>
	/// <returns>The metadata, or null without values.</returns>
	/// <exception cref="ArgumentException">A value has a type that <c>ToMetadataValue</c> does not take, or a key comes twice.</exception>
	public static IReadOnlyDictionary<string, object>? ToMetadata(IEnumerable<KeyValuePair<string, object?>> values)
	{
		Dictionary<string, object>? metadata = null;
		foreach (var pair in values ?? throw new ArgumentNullException(nameof(values)))
		{
			(metadata ??= []).Add(pair.Key, ToMetadataValue(pair.Value)!);
		}
		return metadata;
	}

	// A list of Chroma holds values of one type: an int and a long are both integers, a float and a double both floating-point numbers.
	private static List<object>? ToList(IEnumerable values)
	{
		var list = new List<object>();
		Type? listType = null;
		foreach (var item in values)
		{
			var value = item is null ? throw new ArgumentException("A list in Chroma metadata cannot hold null.", nameof(values)) : ToScalar(item);
			var type = value switch { int or long => typeof(long), float or double => typeof(double), _ => value.GetType() };
			if ((listType ??= type) != type)
			{
				throw new ArgumentException("A list in Chroma metadata holds values of one type: strings, integers, floating-point numbers or Booleans.", nameof(values));
			}
			list.Add(value);
		}
		return list.Count == 0 ? null : list;
	}

	private static object ToScalar(object value)
		=> value switch
		{
			string or bool or int or long or float or double => value,
			DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
			DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
#if NET
			DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
#endif
			_ => throw new ArgumentException($"Chroma metadata has no value of type {value.GetType().Name}.", nameof(value)),
		};

	/// <summary>
	/// The .NET value of the given type for a metadata value, as <c>ChromaMetadataValues.Exact</c> reads it: a <c>long</c>, a
	/// <c>double</c>, a <c>bool</c>, a string, or a list of them. The types are the ones of <c>ToMetadataValue</c>, nullable or not, and
	/// arrays and <c>List&lt;T&gt;</c> of them. An integer reads as any number type, checked for an <c>int</c>; a floating-point number
	/// as a <c>double</c> or a <c>float</c>; a date from its text. A null value is null.
	/// </summary>
	/// <param name="value">The metadata value.</param>
	/// <param name="type">The type to read it as.</param>
	/// <returns>The value of the type, or null.</returns>
	/// <exception cref="InvalidCastException">The value does not convert to the type: another type, a date that does not parse, or an integer beyond an <c>int</c>.</exception>
	public static object? FromMetadataValue(object? value, Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return value switch
		{
			null => null,
			IList list => FromList(list, type),
			_ => FromScalar(value, type),
		};
	}

	private static object FromList(IList list, Type type)
		=> Collection<string>(list, type)
			?? Collection<bool>(list, type)
			?? Collection<int>(list, type)
			?? Collection<long>(list, type)
			?? Collection<float>(list, type)
			?? Collection<double>(list, type)
			?? Collection<DateTime>(list, type)
			?? Collection<DateTimeOffset>(list, type)
#if NET
			?? Collection<DateOnly>(list, type)
#endif
			?? throw CannotRead(list, type);

	// An array or a list of T, or null for another type.
	private static object? Collection<T>(IList list, Type type)
	{
		if (type != typeof(T[]) && type != typeof(List<T>))
		{
			return null;
		}
		var items = new List<T>(list.Count);
		foreach (var item in list)
		{
			items.Add(item is null ? throw CannotRead(list, type) : (T)FromScalar(item, typeof(T)));
		}
		return type == typeof(T[]) ? items.ToArray() : items;
	}

	private static object FromScalar(object value, Type type)
	{
		try
		{
			return (value, type) switch
			{
				(string text, var t) when t == typeof(string) => text,
				(string text, var t) when t == typeof(DateTimeOffset) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
				(string text, var t) when t == typeof(DateTime) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
#if NET
				(string text, var t) when t == typeof(DateOnly) => DateOnly.Parse(text, CultureInfo.InvariantCulture),
#endif
				(bool flag, var t) when t == typeof(bool) => flag,
				(long number, var t) when t == typeof(long) => number,
				(long number, var t) when t == typeof(int) => checked((int)number),
				(long number, var t) when t == typeof(double) => (double)number,
				(long number, var t) when t == typeof(float) => (float)number,
				(double number, var t) when t == typeof(double) => number,
				(double number, var t) when t == typeof(float) => (float)number,
				_ => throw CannotRead(value, type),
			};
		}
		catch (FormatException ex)
		{
			throw CannotRead(value, type, ex);
		}
		catch (OverflowException ex)
		{
			throw CannotRead(value, type, ex);
		}
	}

	private static InvalidCastException CannotRead(object value, Type type, Exception? inner = null)
		=> new($"Cannot read the metadata value of type {value.GetType().Name} as {type.Name}.", inner);
}

namespace ChromaDB.Client.Models;

/// <summary>
/// An index of the values of a collection that a schema turns on or off, with <c>ChromaCollectionSchema.WithIndex</c> and
/// <c>WithoutIndex</c>.
/// </summary>
public enum ChromaSchemaIndex
{
	/// <summary>
	/// The full-text search index of the documents, <c>fts_index</c>, for <c>ChromaWhereDocumentOperator.Contains</c> and the regular
	/// expressions. On by default.
	/// </summary>
	FullTextSearch,
	/// <summary>
	/// The index of the string values of the metadata, <c>string_inverted_index</c>, for the filters on them. On by default.
	/// </summary>
	StringInverted,
	/// <summary>
	/// The index of the integer values of the metadata, <c>int_inverted_index</c>, for the filters on them. On by default.
	/// </summary>
	IntInverted,
	/// <summary>
	/// The index of the floating-point values of the metadata, <c>float_inverted_index</c>, for the filters on them. On by default.
	/// </summary>
	FloatInverted,
	/// <summary>
	/// The index of the Boolean values of the metadata, <c>bool_inverted_index</c>, for the filters on them. On by default.
	/// </summary>
	BoolInverted,
}

namespace SchemaCompare.Models;

/// <summary>
/// One column pair of one foreign key, exactly as <c>sys.foreign_key_columns</c> reports it:
/// a key over two columns arrives as two rows that share <see cref="Name"/>, and pairing them
/// back up is what makes a valid composite <c>ON</c> clause.
/// </summary>
/// <param name="Name">The constraint's own name, so a finding, a tooltip and a
/// <c>sp_helpconstraint</c> output can point at the same key.</param>
/// <param name="Ordinal">The pair's position within the key (<c>constraint_column_id</c>), which
/// is what puts a composite key's columns back in the order they were declared.</param>
public readonly record struct ForeignKeyRef(
    string Name,
    string FromSchema,
    string FromTable,
    string FromColumn,
    string ToSchema,
    string ToTable,
    string ToColumn,
    int Ordinal)
{
    /// <summary>The child side, in the "schema.Name" key the schema cache is indexed by.</summary>
    public string FromKey => $"{FromSchema}.{FromTable}";

    /// <summary>The parent side a key points at.</summary>
    public string ToKey => $"{ToSchema}.{ToTable}";
}

/// <summary>
/// One table as the script names it. <see cref="Key"/> is the resolved <c>schema.Name</c> the
/// foreign keys are matched against; <see cref="Alias"/> is what the query declared, if anything;
/// <see cref="Written"/> is the text as typed, which is the safe prefix for an unaliased table:
/// <c>[dbo].[Order].OrderId</c> resolves wherever the table is bound, while a bare
/// <c>Order.OrderId</c> is a syntax error the moment the name needs brackets.
/// </summary>
public readonly record struct JoinSide(string Key, string? Alias, string Written)
{
    /// <summary>The prefix this table's columns take in the generated clause.</summary>
    public string Ref => string.IsNullOrWhiteSpace(Alias) ? Written : Alias;
}

/// <summary>
/// An <c>ON</c> clause the editor can offer for the join being typed. <see cref="Text"/> is what
/// goes into the script; <see cref="Description"/> names the key it came from, because two tables
/// can be related more than once and the operator has to be able to tell the two offers apart.
/// </summary>
public sealed record JoinSuggestion(string Text, string Description)
{
    public override string ToString() => Text;
}

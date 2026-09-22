namespace SchemaCompare.Models;

public class TableColumn
{
    public int OrdinalPosition { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int? MaxLength { get; set; }
    public int? NumericPrecision { get; set; }
    public int? NumericScale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public string? DefaultValue { get; set; }

    /// <summary>Human-readable data type with length/precision appended.</summary>
    public string DisplayType
    {
        get
        {
            var base_ = DataType.ToUpperInvariant();
            return base_ switch
            {
                "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "BINARY" or "VARBINARY" =>
                    MaxLength.HasValue
                        ? (MaxLength == -1 ? $"{base_}(MAX)" : $"{base_}({MaxLength})")
                        : base_,
                "DECIMAL" or "NUMERIC" =>
                    NumericPrecision.HasValue && NumericScale.HasValue
                        ? $"{base_}({NumericPrecision},{NumericScale})"
                        : base_,
                _ => base_
            };
        }
    }

    public string NullableDisplay => IsNullable ? "NULL" : "NOT NULL";
    public string PkDisplay => IsPrimaryKey ? "✔" : "";
    public string IdentityDisplay => IsIdentity ? "✔" : "";
}

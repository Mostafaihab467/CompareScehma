namespace SchemaCompare.Models;

public class CompareResultSummary
{
    public int AddedCount { get; set; }
    public int ChangedCount { get; set; }
    public int DeletedCount { get; set; }
    public int TotalDifferences => AddedCount + ChangedCount + DeletedCount;
}

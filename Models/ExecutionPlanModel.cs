namespace SchemaCompare.Models;

/// <summary>One operator box in the execution-plan tree (SSMS-style).</summary>
public class PlanNode
{
    public string PhysicalOp { get; init; } = "";
    public string LogicalOp { get; init; } = "";
    /// <summary>Rows the optimizer estimates this operator produces.</summary>
    public double EstimatedRows { get; init; }
    /// <summary>Cumulative optimizer cost of this node plus all its children.</summary>
    public double SubtreeCost { get; init; }
    /// <summary>Single-node cost (SubtreeCost minus children) — the box's own share.</summary>
    public double NodeCost { get; set; }
    /// <summary>Estimated I/O share of the node cost.</summary>
    public double EstimatedIo { get; init; }
    /// <summary>Estimated CPU share of the node cost.</summary>
    public double EstimatedCpu { get; init; }
    /// <summary>Average estimated row size in bytes.</summary>
    public double EstimatedRowSize { get; init; }
    /// <summary>Rows the operator really produced. Null in an estimated plan — nothing ran.</summary>
    public double? ActualRows { get; init; }
    /// <summary>Rows the operator examined to produce those: a scan that read 65,000 to return 5.</summary>
    public double? ActualRowsRead { get; init; }
    /// <summary>Actual CPU time of this operator, in milliseconds.</summary>
    public double? ActualCpuMs { get; init; }
    /// <summary>Actual elapsed time of this operator, in milliseconds.</summary>
    public double? ActualTimeMs { get; init; }
    public double? ActualLogicalReads { get; init; }
    public double? ActualPhysicalReads { get; init; }
    /// <summary>How many times the operator ran; above 1 for a spool or a nested-loop inner side.</summary>
    public int? Executions { get; init; }
    public bool IsParallel { get; init; }
    public string? OrderByDiagnostic { get; init; }
    /// <summary>SubtreeCost as a share of the whole plan, 0-100.</summary>
    public double CostPercent { get; set; }
    public string? ObjectName { get; init; }
    public List<PlanNode> Children { get; } = [];

    /// <summary>Optimizer warnings attached to this operator (spills, conversions…).</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Seek/residual predicate summary, when present (e.g. "WHERE type = 'U'").</summary>
    public string? Predicate { get; set; }

    /// <summary>ORDER BY handled at this operator, when present.</summary>
    public string? SortOrder { get; set; }

    public string Label => PhysicalOp switch
    {
        "" or null => "?",
        "SELECT" => "Result",
        _ => PhysicalOp
    };

    /// <summary>True when the server reported runtime metrics for this operator.</summary>
    public bool HasRuntimeStats => ActualRows.HasValue;

    /// <summary>
    /// How far the optimizer's row estimate missed, always ≥ 1: actual ÷ estimated when it
    /// under-guessed, the inverse when it over-guessed. Null while either side is unusable,
    /// because "estimated 1, got 0" is not a 100× miss — it is an empty result.
    /// </summary>
    public double? EstimateSkew =>
        ActualRows is { } actual && EstimatedRows > 0 && actual > 0
            ? (actual >= EstimatedRows ? actual / EstimatedRows : EstimatedRows / actual)
            : null;

    /// <summary>Depth-first flattened tree in draw order.</summary>
    public IEnumerable<PlanNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }
}

/// <summary>Missing-index hint from the optimizer: impact + a ready CREATE INDEX script.</summary>
public class MissingIndexSuggestion
{
    public double Impact { get; init; }
    public string Table { get; init; } = "";
    public List<string> KeyColumns { get; init; } = [];
    public List<string> IncludedColumns { get; init; } = [];
    public string CreateScript { get; init; } = "";
}

/// <summary>One statement's plan: a small header + the operator tree beneath it.</summary>
public class PlanStatement
{
    public string StatementType { get; init; } = "SELECT";
    /// <summary>The T-SQL text of the statement (truncated for display).</summary>
    public string StatementText { get; init; } = "";
    public PlanNode? Root { get; set; }

    /// <summary>Optimizer confidence: TRIVIAL / SIMPLE / FULL.</summary>
    public string? OptimizationLevel { get; init; }
    public int? DegreeOfParallelism { get; set; }
    /// <summary>Memory reserved for sort/hash, in KB.</summary>
    public int? MemoryGrantKb { get; set; }
    /// <summary>Compiled plan size, in KB.</summary>
    public int? CachedPlanSizeKb { get; set; }
    public double SubtreeCost { get; init; }
    /// <summary>What the statement really took, from QueryTimeStats. Null in an estimated plan.</summary>
    public double? ActualElapsedMs { get; set; }
    public double? ActualCpuMs { get; set; }

    public MissingIndexSuggestion? MissingIndex { get; set; }
}

/// <summary>Parsed result of one or more ShowPlanXML documents.</summary>
public class ExecutionPlan
{
    public List<PlanStatement> Statements { get; } = [];

    /// <summary>Total subtree cost of the most expensive statement — the 100% baseline.</summary>
    public double TotalCost => Statements.Count == 0 ? 0 : Statements.Max(s => s.Root?.SubtreeCost ?? 0);

    /// <summary>
    /// False for an estimated plan: the query was compiled and never run, so every number in
    /// the tree is the optimizer's guess. The diagram says which one the operator is looking at.
    /// </summary>
    public bool HasRuntimeStats => Statements.Any(s =>
        s.Root?.SelfAndDescendants().Any(n => n.HasRuntimeStats) == true);

    /// <summary>The operators whose row estimate missed by at least this factor are flagged.</summary>
    public const double SkewWarningFactor = 10;

    /// <summary>Total rows the whole plan actually returned, when it has runtime stats.</summary>
    public double? ActualRowTotal =>
        HasRuntimeStats ? Statements.Sum(s => s.Root?.ActualRows ?? 0) : null;

    public static string FormatRows(double rows) => rows switch
    {
        >= 1_000_000 => $"{rows / 1_000_000:0.#}M",
        >= 1_000 => $"{rows / 1_000:0.#}K",
        _ => rows.ToString("0.##")
    };
}

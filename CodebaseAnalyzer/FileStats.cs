namespace CodebaseAnalyzer;

/// <summary>File-level counts and method-level syntax metrics; no runtime behavior is inferred.</summary>
public class FileStats {
    /// <summary>Total physical lines; equals code plus empty plus comment lines.</summary>
    public int TotalLines { get; set; }
    /// <summary>Source lines, including mixed code/comment lines and directives.</summary>
    public int CodeLines { get; set; }
    /// <summary>Nonblank lines containing only comments, including documentation comments.</summary>
    public int CommentLines { get; set; }
    /// <summary>Empty or whitespace-only lines, displayed as Empty Lines in reports.</summary>
    public int BlankLines { get; set; }

    public int FlatClasses { get; set; }
    public int NestedClasses { get; set; }
    public int Interfaces { get; set; }
    public int Enums { get; set; }
    public int Structs { get; set; }
    public int Records { get; set; }

    public int TotalFunctions { get; set; }
    public int FuncPub { get; set; }
    public int FuncPriv { get; set; }
    public int FuncProt { get; set; }
    public int FuncInt { get; set; }
    public int FuncPubStat { get; set; }
    public int FuncPrivStat { get; set; }
    public int FuncProtStat { get; set; }
    public int FuncIntStat { get; set; }

    public int TotalVariables { get; set; }
    public int VarsPub { get; set; }
    public int VarsPriv { get; set; }
    public int VarsProt { get; set; }
    public int VarsInt { get; set; }
    public int VarsStat { get; set; }

    public int MaxNestingLevel { get; set; }
    public int MaxCyclomaticComplexity { get; set; } = 1;
    public int TotalComplexity { get; set; }
    public int MaxParameters { get; set; }

    public List<(string Name, int Loc)> ClassDetails { get; } = new();
    public List<MethodStats> MethodDetails { get; } = new();
}

/// <summary>A method's raw syntax complexity and the portion contributed by switch dispatch labels.</summary>
/// <param name="Name">Containing type and method name.</param>
/// <param name="Line">One-based source line.</param>
/// <param name="Complexity">One plus the counted decisions, including switch cases.</param>
/// <param name="DispatchCases">Number of non-default switch labels or expression arms.</param>
public sealed record MethodStats(string Name, int Line, int Complexity, int DispatchCases) {
    /// <summary>Counted decisions inside lambdas, anonymous methods and local functions, excluding their baseline of one.</summary>
    public int NestedCallableDecisions { get; init; }

    /// <summary>Baseline plus decisions directly in the declaring method, excluding nested callable bodies.</summary>
    public int DirectComplexity => Complexity - NestedCallableDecisions;

    /// <summary>True when every counted decision is a switch label, with no other counted branches.</summary>
    public bool IsDispatchOnly => DispatchCases > 0 && Complexity == DispatchCases + 1;
}


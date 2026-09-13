using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodebaseAnalyzer;

public class FileStats {
    public int TotalLines { get; set; }
    public int CodeLines { get; set; }
    public int CommentLines { get; set; }
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
}

public class ProjectInfo {
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Dir { get; set; } = "";
    public List<string> Refs { get; set; } = new();
    public int Loc { get; set; }
    public int Files { get; set; }
}

public class FileSystemNode {
    public bool IsDir { get; set; } = true;
    public Dictionary<string, FileSystemNode> Children { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public FileStats? Stats { get; set; }
}

public class MetricWalker : CSharpSyntaxWalker {
    public FileStats Stats { get; } = new();
    private int _typeDepth = 0;
    private int _blockDepth = 0;
    private Stack<int> _complexityStack = new();
    private Stack<string> _namespaceStack = new();

    public MetricWalker() : base(SyntaxWalkerDepth.Node) {
        _complexityStack.Push(1);
        _namespaceStack.Push("Global");
    }

    private (string access, bool isStatic) GetAccess(SyntaxTokenList modifiers) {
        bool isStatic = modifiers.Any(SyntaxKind.StaticKeyword);
        string access = "private";
        if (modifiers.Any(SyntaxKind.PublicKeyword)) access = "public";
        else if (modifiers.Any(SyntaxKind.ProtectedKeyword)) {
            access = modifiers.Any(SyntaxKind.InternalKeyword) ? "protected internal" : "protected";
        }
        else if (modifiers.Any(SyntaxKind.InternalKeyword)) access = "internal";
        return (access, isStatic);
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node) {
        _namespaceStack.Push(node.Name.ToString());
        base.VisitFileScopedNamespaceDeclaration(node);
        _namespaceStack.Pop();
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node) {
        _namespaceStack.Push(node.Name.ToString());
        base.VisitNamespaceDeclaration(node);
        _namespaceStack.Pop();
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node) {
        if (_typeDepth > 0) Stats.NestedClasses++; else Stats.FlatClasses++;

        var start = node.GetLocation().GetLineSpan().StartLinePosition.Line;
        var end = node.GetLocation().GetLineSpan().EndLinePosition.Line;
        string fullName = $"{_namespaceStack.Peek()}.{node.Identifier.Text}";
        Stats.ClassDetails.Add((fullName, (end - start) + 1));

        _typeDepth++;
        base.VisitClassDeclaration(node);
        _typeDepth--;
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) { Stats.Interfaces++; base.VisitInterfaceDeclaration(node); }
    public override void VisitStructDeclaration(StructDeclarationSyntax node) { Stats.Structs++; base.VisitStructDeclaration(node); }
    public override void VisitRecordDeclaration(RecordDeclarationSyntax node) { Stats.Records++; base.VisitRecordDeclaration(node); }
    public override void VisitEnumDeclaration(EnumDeclarationSyntax node) { Stats.Enums++; base.VisitEnumDeclaration(node); }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
        Stats.TotalFunctions++;
        var (access, isStat) = GetAccess(node.Modifiers);

        if (access == "public") { if (isStat) Stats.FuncPubStat++; else Stats.FuncPub++; }
        else if (access == "protected" || access == "protected internal") { if (isStat) Stats.FuncProtStat++; else Stats.FuncProt++; }
        else if (access == "internal") { if (isStat) Stats.FuncIntStat++; else Stats.FuncInt++; }
        else { if (isStat) Stats.FuncPrivStat++; else Stats.FuncPriv++; }

        int paramCount = node.ParameterList.Parameters.Count;
        if (paramCount > Stats.MaxParameters) Stats.MaxParameters = paramCount;

        _complexityStack.Push(1);
        base.VisitMethodDeclaration(node);
        int methodComp = _complexityStack.Pop();
        Stats.TotalComplexity += methodComp;
        if (methodComp > Stats.MaxCyclomaticComplexity) Stats.MaxCyclomaticComplexity = methodComp;
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node) {
        Stats.TotalVariables += node.Declaration.Variables.Count;
        var (access, isStat) = GetAccess(node.Modifiers);
        if (isStat) Stats.VarsStat += node.Declaration.Variables.Count;
        if (access == "public") Stats.VarsPub += node.Declaration.Variables.Count;
        else if (access == "protected" || access == "protected internal") Stats.VarsProt += node.Declaration.Variables.Count;
        else if (access == "internal") Stats.VarsInt += node.Declaration.Variables.Count;
        else Stats.VarsPriv += node.Declaration.Variables.Count;
        base.VisitFieldDeclaration(node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
        Stats.TotalVariables++;
        var (access, isStat) = GetAccess(node.Modifiers);
        if (isStat) Stats.VarsStat++;
        if (access == "public") Stats.VarsPub++;
        else if (access == "protected" || access == "protected internal") Stats.VarsProt++;
        else if (access == "internal") Stats.VarsInt++;
        else Stats.VarsPriv++;
        base.VisitPropertyDeclaration(node);
    }

    public override void VisitBlock(BlockSyntax node) {
        _blockDepth++;
        if (_blockDepth > Stats.MaxNestingLevel) Stats.MaxNestingLevel = _blockDepth;
        base.VisitBlock(node);
        _blockDepth--;
    }

    public override void Visit(SyntaxNode? node) {
        if (node == null) return;
        if (node is IfStatementSyntax || node is ForStatementSyntax || node is ForEachStatementSyntax ||
            node is WhileStatementSyntax || node is DoStatementSyntax || node is CatchClauseSyntax ||
            node is CaseSwitchLabelSyntax || node is ConditionalExpressionSyntax) {
            _complexityStack.Push(_complexityStack.Pop() + 1);
        } else if (node is BinaryExpressionSyntax binExpr &&
                    (binExpr.IsKind(SyntaxKind.LogicalAndExpression) || binExpr.IsKind(SyntaxKind.LogicalOrExpression))) {
            _complexityStack.Push(_complexityStack.Pop() + 1);
        }
        base.Visit(node);
    }
}

public class Program {
    static readonly HashSet<string> IgnoreDirs = new(StringComparer.OrdinalIgnoreCase) {
        "bin", "obj", ".git", ".idea", ".vscode", ".history", "engineapps",
        "remakeenginedocs", "enginebuild", "schemas", ".bettergit", ".github", "enginenettest"
    };

    public static void Main(string[] args) {
        bool functionMode = args.Any(a => a.Equals("--functions", StringComparison.OrdinalIgnoreCase) || a.Equals("-f", StringComparison.OrdinalIgnoreCase));
        string targetDir = args.FirstOrDefault(a => !a.StartsWith("-")) ?? (Directory.Exists("../EngineNet") ? "../EngineNet" : "../");

        var (projectsDict, sortedProjs) = DiscoverProjects(targetDir);

        if (functionMode) {
            RunFunctionAnalysis(targetDir, sortedProjs);
            return;
        }

        Console.WriteLine($"\nAnalyzing structure and metrics for: {Path.GetFullPath(targetDir)}...\n");

        var allStats = new FileStats();
        var alerts = new List<string>();
        var classRegistry = new Dictionary<string, int>();
        var fileCounts = new Dictionary<string, (int classes, int loc, List<(string, int)> details)>();
        var treeRoot = new FileSystemNode();


        foreach (var filePath in GetFilesValid(targetDir)) {
            var stats = AnalyzeSingleFile(filePath);
            AddToTree(treeRoot, Path.GetRelativePath(targetDir, filePath), stats);

            allStats.TotalLines += stats.TotalLines; allStats.CodeLines += stats.CodeLines;
            allStats.CommentLines += stats.CommentLines; allStats.BlankLines += stats.BlankLines;
            allStats.FlatClasses += stats.FlatClasses; allStats.NestedClasses += stats.NestedClasses;
            allStats.Interfaces += stats.Interfaces; allStats.Enums += stats.Enums;
            allStats.Structs += stats.Structs; allStats.Records += stats.Records;

            allStats.TotalFunctions += stats.TotalFunctions;
            allStats.FuncPub += stats.FuncPub; allStats.FuncPubStat += stats.FuncPubStat;
            allStats.FuncPriv += stats.FuncPriv; allStats.FuncPrivStat += stats.FuncPrivStat;
            allStats.FuncProt += stats.FuncProt; allStats.FuncProtStat += stats.FuncProtStat;
            allStats.FuncInt += stats.FuncInt; allStats.FuncIntStat += stats.FuncIntStat;

            allStats.TotalVariables += stats.TotalVariables;
            allStats.VarsPub += stats.VarsPub; allStats.VarsPriv += stats.VarsPriv;
            allStats.VarsStat += stats.VarsStat;

            if (stats.MaxNestingLevel > allStats.MaxNestingLevel) allStats.MaxNestingLevel = stats.MaxNestingLevel;
            if (stats.MaxCyclomaticComplexity > allStats.MaxCyclomaticComplexity) allStats.MaxCyclomaticComplexity = stats.MaxCyclomaticComplexity;
            if (stats.MaxParameters > allStats.MaxParameters) allStats.MaxParameters = stats.MaxParameters;

            string fileName = Path.GetFileName(filePath);
            if (stats.MaxCyclomaticComplexity > 15) alerts.Add($"[COMPLEXITY] {fileName} (Max: {stats.MaxCyclomaticComplexity})");
            if (stats.MaxNestingLevel > 5) alerts.Add($"[DEEP NESTING] {fileName} (Max Depth: {stats.MaxNestingLevel})");
            if (stats.TotalLines > 600) alerts.Add($"[GOD FILE] {fileName} (Lines: {stats.TotalLines})");

            var owner = GetOwningProject(filePath, sortedProjs);
            if (owner != null) { owner.Loc += stats.TotalLines; owner.Files++; }

            fileCounts[filePath] = (stats.FlatClasses + stats.NestedClasses, stats.TotalLines, stats.ClassDetails);

            foreach (var cls in stats.ClassDetails) {
                if (classRegistry.ContainsKey(cls.Name)) classRegistry[cls.Name] += cls.Loc;
                else classRegistry[cls.Name] = cls.Loc;
            }
        }

        PrintProjectGraph(projectsDict);
        Console.WriteLine(new string('=', 105));
        Console.WriteLine(" 📂 FILE SYSTEM TREE");
        Console.WriteLine(new string('=', 105));
        PrintTreeNode(Path.GetFileName(Path.GetFullPath(targetDir)), treeRoot, isRoot: true);

        PrintSummary(allStats, classRegistry, fileCounts, alerts);
    }

    #region Standard Analysis Methods

    static (Dictionary<string, ProjectInfo>, List<ProjectInfo>) DiscoverProjects(string rootDir) {
        var csprojFiles = new Dictionary<string, ProjectInfo>();
        var projRefPattern = new Regex(@"<ProjectReference\s+Include\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);

        foreach (var file in Directory.GetFiles(rootDir, "*.csproj", SearchOption.AllDirectories)) {
            if (IgnoreDirs.Any(d => file.Contains($"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))) continue;

            var absPath = Path.GetFullPath(file);
            csprojFiles[absPath] = new ProjectInfo {
                Name = Path.GetFileNameWithoutExtension(file),
                Path = absPath, Dir = Path.GetDirectoryName(absPath)!
            };
        }

        foreach (var proj in csprojFiles.Values) {
            try {
                string content = File.ReadAllText(proj.Path);
                foreach (Match m in projRefPattern.Matches(content)) {
                    string refPath = Path.GetFullPath(Path.Combine(proj.Dir, m.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)));
                    if (csprojFiles.ContainsKey(refPath)) proj.Refs.Add(csprojFiles[refPath].Name);
                    else proj.Refs.Add(Path.GetFileNameWithoutExtension(refPath) + " (External)");
                }
            } catch { }
        }

        return (csprojFiles, csprojFiles.Values.OrderByDescending(p => p.Dir.Length).ToList());
    }

    public static ProjectInfo? GetOwningProject(string filepath, List<ProjectInfo> sortedProjs) {
        string absPath = Path.GetFullPath(filepath);
        return sortedProjs.FirstOrDefault(p => absPath.StartsWith(p.Dir + Path.DirectorySeparatorChar) || absPath == p.Dir);
    }

    static void AddToTree(FileSystemNode root, string relPath, FileStats stats) {
        var parts = relPath.Split(Path.DirectorySeparatorChar);
        var current = root;
        for (int i = 0; i < parts.Length - 1; i++) {
            if (!current.Children.ContainsKey(parts[i])) current.Children[parts[i]] = new FileSystemNode();
            current = current.Children[parts[i]];
        }
        current.Children[parts.Last()] = new FileSystemNode { IsDir = false, Stats = stats };
    }

    static void PrintProjectGraph(Dictionary<string, ProjectInfo> projectsDict) {
        Console.WriteLine(new string('=', 105));
        Console.WriteLine(" 📦 SUBPROJECT ARCHITECTURE & DEPENDENCY GRAPH");
        Console.WriteLine(new string('=', 105));
        if (projectsDict.Count == 0) { Console.WriteLine("No .csproj files found.\n"); return; }

        foreach (var p in projectsDict.Values.OrderBy(x => x.Name)) {
            Console.WriteLine($" {p.Name} ({p.Files} files, {p.Loc} lines)");
            if (p.Refs.Count > 0) {
                for (int i = 0; i < p.Refs.Count; i++) {
                    Console.WriteLine($"   {(i == p.Refs.Count - 1 ? "└─" : "├─")} Depends on: {p.Refs[i]}");
                }
            } else Console.WriteLine("   └─ No internal project references.");
            Console.WriteLine();
        }
    }

    static void PrintTreeNode(string name, FileSystemNode node, string prefix = "", bool isLastDir = true, bool isRoot = false) {
        if (isRoot) { Console.WriteLine($"📁 {name}"); prefix = ""; }
        else {
            Console.WriteLine($"{prefix}{(isLastDir ? "\\---" : "+---")}{name}");
            prefix += isLastDir ? "    " : "|   ";
        }

        if (node.IsDir) {
            var files = node.Children.Where(kv => !kv.Value.IsDir).OrderBy(kv => kv.Key).ToList();
            var dirs = node.Children.Where(kv => kv.Value.IsDir).OrderBy(kv => kv.Key).ToList();

            foreach (var f in files) {
                Console.WriteLine($"{prefix}{f.Key}");
                var stats = f.Value.Stats!;
                string ind = prefix + "    ";
                float avgComp = stats.TotalFunctions > 0 ? (float)stats.TotalComplexity / stats.TotalFunctions : 1f;

                Console.WriteLine($"{ind}├─ Base:  L: {stats.TotalLines} ({stats.CodeLines}C, {stats.CommentLines}#) | Cls: {stats.FlatClasses} Flat, {stats.NestedClasses} Nested | Int: {stats.Interfaces} | Enum: {stats.Enums}");
                Console.WriteLine($"{ind}├─ Funcs: {stats.TotalFunctions} (Pub: {stats.FuncPub}+{stats.FuncPubStat}S, Priv: {stats.FuncPriv}+{stats.FuncPrivStat}S, Prot: {stats.FuncProt}+{stats.FuncProtStat}S, Int: {stats.FuncInt}+{stats.FuncIntStat}S)");
                Console.WriteLine($"{ind}├─ Vars:  {stats.TotalVariables} (Pub: {stats.VarsPub}, Priv: {stats.VarsPriv}, Stat: {stats.VarsStat})");
                Console.WriteLine($"{ind}└─ Evals: Max Nesting: {stats.MaxNestingLevel} | Max/Avg Complexity: {stats.MaxCyclomaticComplexity}/{avgComp:F1} | Max Params: {stats.MaxParameters}\n{prefix}");
            }

            for (int i = 0; i < dirs.Count; i++) {
                PrintTreeNode(dirs[i].Key, dirs[i].Value, prefix, i == dirs.Count - 1);
            }
        }
    }

    static IEnumerable<string> GetFilesValid(string rootPath) {
        var files = new List<string>();
        var dirs = new Queue<string>();
        dirs.Enqueue(rootPath);
        while (dirs.Count > 0) {
            string currentDir = dirs.Dequeue();
            try {
                foreach (string dir in Directory.GetDirectories(currentDir)) {
                    if (!IgnoreDirs.Contains(Path.GetFileName(dir))) dirs.Enqueue(dir);
                }
                foreach (string file in Directory.GetFiles(currentDir, "*.cs")) {
                    if (!file.EndsWith(".Designer.cs")) files.Add(file);
                }
            } catch (UnauthorizedAccessException) { }
        }
        return files;
    }

    static FileStats AnalyzeSingleFile(string filepath) {
        string code = File.ReadAllText(filepath);
        var stats = new FileStats();
        var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        stats.TotalLines = lines.Length;
        bool inBlock = false;

        foreach (var line in lines) {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) stats.BlankLines++;
            else if (trimmed.Contains("/*") && trimmed.Contains("*/")) stats.CommentLines++;
            else if (trimmed.Contains("/*")) { inBlock = true; stats.CommentLines++; }
            else if (trimmed.Contains("*/")) { inBlock = false; stats.CommentLines++; }
            else if (inBlock || trimmed.StartsWith("//")) stats.CommentLines++;
            else stats.CodeLines++;
        }

        var tree = CSharpSyntaxTree.ParseText(code);
        var walker = new MetricWalker();
        walker.Visit(tree.GetCompilationUnitRoot());

        stats.FlatClasses = walker.Stats.FlatClasses; stats.NestedClasses = walker.Stats.NestedClasses;
        stats.Interfaces = walker.Stats.Interfaces; stats.Enums = walker.Stats.Enums;
        stats.Structs = walker.Stats.Structs; stats.Records = walker.Stats.Records;

        stats.TotalFunctions = walker.Stats.TotalFunctions;
        stats.FuncPub = walker.Stats.FuncPub; stats.FuncPubStat = walker.Stats.FuncPubStat;
        stats.FuncPriv = walker.Stats.FuncPriv; stats.FuncPrivStat = walker.Stats.FuncPrivStat;
        stats.FuncProt = walker.Stats.FuncProt; stats.FuncProtStat = walker.Stats.FuncProtStat;
        stats.FuncInt = walker.Stats.FuncInt; stats.FuncIntStat = walker.Stats.FuncIntStat;

        stats.TotalVariables = walker.Stats.TotalVariables;
        stats.VarsPub = walker.Stats.VarsPub; stats.VarsPriv = walker.Stats.VarsPriv;
        stats.VarsStat = walker.Stats.VarsStat;

        stats.MaxNestingLevel = walker.Stats.MaxNestingLevel;
        stats.MaxCyclomaticComplexity = walker.Stats.MaxCyclomaticComplexity;
        stats.TotalComplexity = walker.Stats.TotalComplexity;
        stats.MaxParameters = walker.Stats.MaxParameters;
        stats.ClassDetails.AddRange(walker.Stats.ClassDetails);

        return stats;
    }

    static void PrintSummary(FileStats stats, Dictionary<string, int> classReg, Dictionary<string, (int cls, int loc, List<(string, int)> det)> fileCounts, List<string> alerts) {
        Console.WriteLine(new string('=', 105));
        Console.WriteLine(" 📊 GLOBAL CODEBASE SUMMARY");
        Console.WriteLine(new string('=', 105));
        Console.WriteLine($"Total Lines:        {stats.TotalLines} ({stats.CodeLines} Code, {stats.CommentLines} Comments, {stats.BlankLines} Blank)");
        Console.WriteLine($"Architecture:       {stats.FlatClasses} Flat Classes, {stats.NestedClasses} Nested Classes");
        Console.WriteLine($"                    {stats.Interfaces} Interfaces, {stats.Enums} Enums, {stats.Structs} Structs, {stats.Records} Records");

        int statTot = stats.FuncPubStat + stats.FuncPrivStat + stats.FuncProtStat + stats.FuncIntStat;
        Console.WriteLine($"Total Functions:    {stats.TotalFunctions} (Stat: {statTot})");
        Console.WriteLine($"                    Public:    {stats.FuncPub + stats.FuncPubStat,-5} ({stats.FuncPub} Inst, {stats.FuncPubStat} Stat)");
        Console.WriteLine($"                    Private:   {stats.FuncPriv + stats.FuncPrivStat,-5} ({stats.FuncPriv} Inst, {stats.FuncPrivStat} Stat)");

        Console.WriteLine(new string('-', 105));
        Console.WriteLine(" 🚀 CODE QUALITY EVALUATIONS (Highest recorded values across project)");
        Console.WriteLine(new string('-', 105));
        Console.WriteLine($"Deepest Nest Level:          {stats.MaxNestingLevel}");
        Console.WriteLine($"Max Cyclomatic Complexity:   {stats.MaxCyclomaticComplexity}");
        Console.WriteLine($"Largest Number of Params:    {stats.MaxParameters}");

        Console.WriteLine(new string('-', 105));
        Console.WriteLine(" 🏆 TOP 10 LARGEST CLASSES (Combined Partial Across Files)");
        Console.WriteLine(new string('-', 105));
        foreach (var kvp in classReg.OrderByDescending(k => k.Value).Take(10).Select((v, i) => new { v, i })) {
            Console.WriteLine($"{kvp.i + 1,2}. {kvp.v.Key,-65} {kvp.v.Value,6} lines");
        }

        Console.WriteLine(new string('-', 105));
        Console.WriteLine(" 📁 FILES WITH THE MOST CLASSES");
        Console.WriteLine(new string('-', 105));
        foreach (var kvp in fileCounts.OrderByDescending(k => k.Value.cls).Take(5).Select((v, i) => new { v, i })) {
            Console.WriteLine($"{kvp.i + 1,2}. {Path.GetFileName(kvp.v.Key),-65} {kvp.v.Value.cls,3} classes ({kvp.v.Value.loc} lines)");
        }

        Console.WriteLine(new string('-', 105));
        Console.WriteLine(" ⚠️ ACTIONABLE ALERTS (Refactoring Targets)");
        Console.WriteLine(new string('-', 105));
        if (alerts.Count > 0) foreach (var a in alerts.OrderBy(a => a)) Console.WriteLine($"  * {a}");
        else Console.WriteLine("  * None! Codebase is looking healthy against current thresholds.");
        Console.WriteLine(new string('=', 105) + "\n");
    }

    #endregion

    #region Deep Function Analysis

    static void RunFunctionAnalysis(string targetDir, List<ProjectInfo> sortedProjs) {
        Console.WriteLine($"\n🔍 Running Deep Semantic Function Analysis for: {Path.GetFullPath(targetDir)}...\n");
        Console.WriteLine("Building memory compilation... (This may take a moment to parse the syntax trees)");

        var files = GetFilesValid(targetDir).ToList();
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)).ToList();

        // Include basic standard libs to prevent completely broken semantic models
        var references = new[] {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create("FuncAnalysisComp", trees, references);
        var methods = new Dictionary<IMethodSymbol, MethodUsageInfo>(SymbolEqualityComparer.Default);

        // Pass 1: Catalog all defined methods
        foreach (var tree in trees) {
            var model = compilation.GetSemanticModel(tree);
            var walker = new MethodDeclarationWalker(model, methods);
            walker.Visit(tree.GetCompilationUnitRoot());
        }

        // Pass 2: Tally all references & callers
        foreach (var tree in trees) {
            var model = compilation.GetSemanticModel(tree);
            var walker = new MethodReferenceWalker(model, methods, sortedProjs);
            walker.Visit(tree.GetCompilationUnitRoot());
        }

        // Pass 3: Propagate reference counts and callers from Interfaces/Base classes to Implementations
        foreach (var methodInfo in methods.Values) {
            var symbol = methodInfo.Symbol;

            // 1. Inherit from Overrides
            var currentBase = symbol.OverriddenMethod;
            while (currentBase != null) {
                if (methods.TryGetValue(currentBase.OriginalDefinition, out var baseUsage)) {
                    methodInfo.ReferenceCount += baseUsage.ReferenceCount;
                    methodInfo.CallerProjects.UnionWith(baseUsage.CallerProjects);
                    methodInfo.CallerTypes.UnionWith(baseUsage.CallerTypes);
                }
                currentBase = currentBase.OverriddenMethod;
            }

            // 2. Inherit from Interfaces
            var containingType = symbol.ContainingType;
            if (containingType != null) {
                foreach (var iface in containingType.AllInterfaces) {
                    foreach (var member in iface.GetMembers().OfType<IMethodSymbol>()) {
                        var impl = containingType.FindImplementationForInterfaceMember(member);
                        if (SymbolEqualityComparer.Default.Equals(impl, symbol)) {
                            if (methods.TryGetValue(member.OriginalDefinition, out var ifaceUsage)) {
                                methodInfo.ReferenceCount += ifaceUsage.ReferenceCount;
                                methodInfo.CallerProjects.UnionWith(ifaceUsage.CallerProjects);
                                methodInfo.CallerTypes.UnionWith(ifaceUsage.CallerTypes);
                            }
                        }
                    }
                }
            }
        }

        var groupedMethods = methods.Values
            .GroupBy(m => m.Symbol.ContainingType?.ToDisplayString() ?? "Global")
            .OrderBy(g => g.Key);

        Console.WriteLine(new string('=', 115));
        Console.WriteLine(" 🔬 FUNCTION LEVEL ANALYSIS & OPTIMIZATIONS");
        Console.WriteLine(new string('=', 115));

        int unusedCount = 0;
        int overExposedCount = 0;

        foreach (var group in groupedMethods) {
            bool classPrinted = false;

            foreach (var info in group.OrderBy(x => x.Symbol.Name)) {
                string current = GetCurrentAccess(info.Symbol);
                string suggested = GetSuggestedAccess(info, sortedProjs);

                bool isUnused = suggested == "Unused";
                bool canDowngrade = !isUnused && suggested != "Keep" && AccessScore(suggested) < AccessScore(current);

                if (isUnused) unusedCount++;
                if (canDowngrade) overExposedCount++;

                if (!classPrinted) {
                    Console.WriteLine($"\n 📦 {group.Key}");
                    classPrinted = true;
                }

                string alert = isUnused ? "[UNUSED]" : (canDowngrade ? $"[DOWNGRADE TO {suggested.ToUpper()}]" : "[OK]");
                string paddedName = info.Symbol.Name.PadRight(35);
                if (paddedName.Length > 35) paddedName = paddedName.Substring(0, 32) + "...";

                Console.ForegroundColor = isUnused ? ConsoleColor.Red : (canDowngrade ? ConsoleColor.Yellow : ConsoleColor.DarkGray);
                Console.WriteLine($"    ├─ {current,-10} {paddedName} | Refs: {info.ReferenceCount,-4} | {alert}");
                Console.ResetColor();
            }
        }

        Console.WriteLine(new string('=', 115));
        Console.WriteLine($" Total Functions Scanned: {methods.Count}");
        Console.WriteLine($" Potential Unused Methods:{unusedCount, 5}");
        Console.WriteLine($" Over-exposed Methods:    {overExposedCount, 5}");
        Console.WriteLine(new string('=', 115));
        Console.WriteLine("* Note: Unused methods may be Reflection targets, API endpoints, or Engine callbacks (e.g. Update).\n");
    }

    static string GetCurrentAccess(IMethodSymbol symbol) {
        return symbol.DeclaredAccessibility switch {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            _ => "private"
        };
    }

    static int AccessScore(string access) {
        return access switch {
            "public" => 5,
            "protected internal" => 4,
            "internal" => 3,
            "protected" => 2,
            "private protected" => 2,
            "private" => 1,
            _ => 0
        };
    }

    static string GetSuggestedAccess(MethodUsageInfo info, List<ProjectInfo> sortedProjs) {
        string name = info.Symbol.Name;
        // Ignore standard engine/framework callbacks that shouldn't be constrained
        if (name is "Main" or "Awake" or "Start" or "Update" or "LateUpdate" or "FixedUpdate" or "OnEnable" or "OnDisable" or "OnDestroy" or "init")
            return "Keep";

        // Contracts and Overrides cannot have their access modifiers changed without breaking compilation
        if (info.Symbol.IsOverride || info.Symbol.IsVirtual || info.Symbol.IsAbstract) return "Keep";
        if (info.Symbol.ExplicitInterfaceImplementations.Any()) return "Keep";

        var containingType = info.Symbol.ContainingType;
        if (containingType != null) {
            foreach (var iface in containingType.AllInterfaces) {
                foreach (var member in iface.GetMembers().OfType<IMethodSymbol>()) {
                    var impl = containingType.FindImplementationForInterfaceMember(member);
                    if (SymbolEqualityComparer.Default.Equals(impl, info.Symbol)) return "Keep";
                }
            }
        }

        // --- SUPPRESSION CHECK ---
        bool isUnusedSuppressed = HasSuppression(info.Symbol, "Unused");
        bool isVisibilitySuppressed = HasSuppression(info.Symbol, "CanBePrivate") || HasSuppression(info.Symbol, "CanBeInternal") || HasSuppression(info.Symbol, "CanBeProtected");

        // Now that we know it's not bound by a strict compiler contract, we can check if it's unused
        if (info.ReferenceCount == 0 && !isUnusedSuppressed) return "Unused";
        if (info.ReferenceCount == 0 && isUnusedSuppressed) return "Keep";

        // If the method has a visibility suppression, we abort any downgrades.
        if (isVisibilitySuppressed) return "Keep";

        // --- PROJECT BOUNDARY CROSS-CHECK ---
        string methodFile = info.Symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? "";
        var methodProj = GetOwningProject(methodFile, sortedProjs);
        string methodProjName = methodProj?.Name ?? "UnknownProject";

        // If called from outside its own .csproj directory, keep it whatever it is!
        if (info.CallerProjects.Count > 0 && !info.CallerProjects.All(p => p == methodProjName))
            return "Keep";

        // Only accessible in the same class
        if (info.CallerTypes.Count > 0 && info.CallerTypes.All(t => SymbolEqualityComparer.Default.Equals(t, containingType)))
            return "private";

        // Only accessed from derivations
        if (info.CallerTypes.Count > 0 && info.CallerTypes.All(t => DerivesFrom(t, containingType)))
            return "protected";

        // It is accessed globally but entirely inside its native .csproj
        return "internal";
    }

    static bool HasSuppression(ISymbol symbol, string keyword) {
        var current = symbol;
        while (current != null) {
            foreach (var attr in current.GetAttributes()) {
                if (attr.AttributeClass?.Name == "SuppressMessageAttribute" || attr.AttributeClass?.Name == "SuppressMessage") {
                    foreach (var arg in attr.ConstructorArguments) {
                        if (arg.Value is string s && s.Contains(keyword, StringComparison.OrdinalIgnoreCase)) {
                            return true;
                        }
                    }
                }
            }
            // Propagate checks upwards so class-level suppressions apply to their members
            current = current.ContainingType;
        }
        return false;
    }

    static bool DerivesFrom(ITypeSymbol? type, ITypeSymbol? baseType) {
        if (baseType == null) return false;
        
        var current = type?.BaseType;
        while (current != null) {
            if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
            current = current.BaseType;
        }
        return false;
    }

    #endregion
}

#region Semantic Walker Classes

public class MethodUsageInfo {
    public IMethodSymbol Symbol { get; set; } = null!;
    public int ReferenceCount { get; set; }
    public HashSet<ITypeSymbol> CallerTypes { get; } = new(SymbolEqualityComparer.Default);
    public HashSet<string> CallerProjects { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public class MethodDeclarationWalker : CSharpSyntaxWalker {
    private readonly SemanticModel _model;
    private readonly Dictionary<IMethodSymbol, MethodUsageInfo> _methods;

    public MethodDeclarationWalker(SemanticModel model, Dictionary<IMethodSymbol, MethodUsageInfo> methods) {
        _model = model;
        _methods = methods;
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node) {
        if (_model.GetDeclaredSymbol(node) is IMethodSymbol symbol) {
            _methods[symbol] = new MethodUsageInfo { Symbol = symbol };
        }
        base.VisitMethodDeclaration(node);
    }
}

public class MethodReferenceWalker : CSharpSyntaxWalker {
    private readonly SemanticModel _model;
    private readonly Dictionary<IMethodSymbol, MethodUsageInfo> _methods;
    private readonly List<ProjectInfo> _sortedProjs;

    public MethodReferenceWalker(SemanticModel model, Dictionary<IMethodSymbol, MethodUsageInfo> methods, List<ProjectInfo> sortedProjs) {
        _model = model;
        _methods = methods;
        _sortedProjs = sortedProjs;
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node) {
        CheckSymbol(node);
        base.VisitIdentifierName(node);
    }

    public override void VisitGenericName(GenericNameSyntax node) {
        CheckSymbol(node);
        base.VisitGenericName(node);
    }

    private void CheckSymbol(SimpleNameSyntax node) {
        var info = _model.GetSymbolInfo(node);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

        if (symbol is IMethodSymbol methodSymbol) {
            methodSymbol = methodSymbol.OriginalDefinition;

            if (_methods.TryGetValue(methodSymbol, out var usage)) {
                usage.ReferenceCount++;

                var caller = _model.GetEnclosingSymbol(node.SpanStart);
                if (caller != null) {
                    if (caller.ContainingType != null) usage.CallerTypes.Add(caller.ContainingType);
                }

                // Locate the calling file and cross-reference its .csproj ownership
                var callerFile = node.SyntaxTree.FilePath;
                if (!string.IsNullOrEmpty(callerFile)) {
                    var callerProj = Program.GetOwningProject(callerFile, _sortedProjs);
                    if (callerProj != null) usage.CallerProjects.Add(callerProj.Name);
                }
            }
        }
    }
}

#endregion
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodebaseAnalyzer;

/// <summary>
/// Collects syntax metrics, distinguishing pure dispatch and decisions in nested callables.
/// These are source metrics; they do not establish runtime reachability or safety.
/// </summary>
public class MetricWalker : CSharpSyntaxWalker {
    public FileStats Stats { get; } = new();
    private int TypeDepth;
    private int BlockDepth;
    private readonly Stack<MethodScope> MethodScopes = new();
    private readonly Stack<string> Namespaces = new();

    public MetricWalker() : base(SyntaxWalkerDepth.Node) {
        MethodScopes.Push(new MethodScope());
    }

    /// <summary>Interface members are implicitly public unless an explicit accessibility or implementation overrides it.</summary>
    private static (string Access, bool IsStatic) GetAccess(SyntaxTokenList Modifiers, SyntaxNode Node) {
        bool IsStatic = Modifiers.Any(SyntaxKind.StaticKeyword) || Modifiers.Any(SyntaxKind.ConstKeyword);
        string Access;
        if (Modifiers.Any(SyntaxKind.PublicKeyword)) {
            Access = "public";
        } else if (Modifiers.Any(SyntaxKind.ProtectedKeyword)) {
            Access = Modifiers.Any(SyntaxKind.PrivateKeyword) ? "private protected" :
                Modifiers.Any(SyntaxKind.InternalKeyword) ? "protected internal" : "protected";
        } else if (Modifiers.Any(SyntaxKind.InternalKeyword)) {
            Access = "internal";
        } else if (Modifiers.Any(SyntaxKind.PrivateKeyword) || Node is MethodDeclarationSyntax { ExplicitInterfaceSpecifier: not null }) {
            Access = "private";
        } else {
            Access = Node.Parent is InterfaceDeclarationSyntax ? "public" : "private";
        }
        return (Access, IsStatic);
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax Node) {
        Namespaces.Push(Node.Name.ToString());
        base.VisitFileScopedNamespaceDeclaration(Node);
        Namespaces.Pop();
    }

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax Node) {
        Namespaces.Push(Node.Name.ToString());
        base.VisitNamespaceDeclaration(Node);
        Namespaces.Pop();
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax Node) {
        if (TypeDepth > 0) {
            Stats.NestedClasses++;
        } else {
            Stats.FlatClasses++;
        }
        FileLinePositionSpan Location = Node.GetLocation().GetLineSpan();
        string Namespace = Namespaces.Count > 0 ? string.Join('.', Namespaces.Reverse()) : "Global";
        string TypeName = string.Join('.', Node.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Reverse().Select(Type => Type.Identifier.Text));
        Stats.ClassDetails.Add(($"{Namespace}.{TypeName}", Location.EndLinePosition.Line - Location.StartLinePosition.Line + 1));
        TypeDepth++;
        base.VisitClassDeclaration(Node);
        TypeDepth--;
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax Node) {
        Stats.Interfaces++;
        TypeDepth++;
        base.VisitInterfaceDeclaration(Node);
        TypeDepth--;
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax Node) {
        Stats.Structs++;
        TypeDepth++;
        base.VisitStructDeclaration(Node);
        TypeDepth--;
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax Node) {
        Stats.Records++;
        TypeDepth++;
        base.VisitRecordDeclaration(Node);
        TypeDepth--;
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax Node) {
        Stats.Enums++;
        base.VisitEnumDeclaration(Node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax Node) {
        Stats.TotalFunctions++;
        (string Access, bool IsStatic) = GetAccess(Node.Modifiers, Node);
        switch (Access, IsStatic) {
            case ("public", false): Stats.FuncPub++; break;
            case ("public", true): Stats.FuncPubStat++; break;
            case ("protected" or "protected internal" or "private protected", false): Stats.FuncProt++; break;
            case ("protected" or "protected internal" or "private protected", true): Stats.FuncProtStat++; break;
            case ("internal", false): Stats.FuncInt++; break;
            case ("internal", true): Stats.FuncIntStat++; break;
            case (_, false): Stats.FuncPriv++; break;
            case (_, true): Stats.FuncPrivStat++; break;
        }
        Stats.MaxParameters = Math.Max(Stats.MaxParameters, Node.ParameterList.Parameters.Count);
        MethodScope Scope = new();
        MethodScopes.Push(Scope);
        base.VisitMethodDeclaration(Node);
        MethodScopes.Pop();
        Stats.TotalComplexity += Scope.Complexity;
        Stats.MaxCyclomaticComplexity = Math.Max(Stats.MaxCyclomaticComplexity, Scope.Complexity);
        string TypeName = string.Join('.', Node.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(Type => Type.Identifier.Text));
        Stats.MethodDetails.Add(new MethodStats(
            $"{TypeName}.{Node.Identifier.Text}", Node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Scope.Complexity, Scope.DispatchCases
        ) { NestedCallableDecisions = Scope.NestedCallableDecisions });
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax Node) {
        CountVariables(Node.Modifiers, Node, Node.Declaration.Variables.Count);
        base.VisitFieldDeclaration(Node);
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax Node) {
        CountVariables(Node.Modifiers, Node, 1);
        base.VisitPropertyDeclaration(Node);
    }

    private void CountVariables(SyntaxTokenList Modifiers, SyntaxNode Node, int Count) {
        Stats.TotalVariables += Count;
        (string Access, bool IsStatic) = GetAccess(Modifiers, Node);
        if (IsStatic) {
            Stats.VarsStat += Count;
        }
        switch (Access) {
            case "public": Stats.VarsPub += Count; break;
            case "protected" or "protected internal" or "private protected": Stats.VarsProt += Count; break;
            case "internal": Stats.VarsInt += Count; break;
            default: Stats.VarsPriv += Count; break;
        }
    }

    public override void VisitBlock(BlockSyntax Node) {
        BlockDepth++;
        Stats.MaxNestingLevel = Math.Max(Stats.MaxNestingLevel, BlockDepth);
        base.VisitBlock(Node);
        BlockDepth--;
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax Node) => VisitNestedCallable(() => base.VisitSimpleLambdaExpression(Node));
    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax Node) => VisitNestedCallable(() => base.VisitParenthesizedLambdaExpression(Node));
    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax Node) => VisitNestedCallable(() => base.VisitAnonymousMethodExpression(Node));
    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax Node) => VisitNestedCallable(() => base.VisitLocalFunctionStatement(Node));

    /// <summary>Tracks nested callable decisions separately while retaining the original inclusive metric.</summary>
    private void VisitNestedCallable(Action VisitBody) {
        MethodScope Scope = MethodScopes.Peek();
        Scope.NestedCallableDepth++;
        VisitBody();
        Scope.NestedCallableDepth--;
    }

    /// <summary>Retains raw branch counts, including modern switch arms; dispatch labels are also recorded separately.</summary>
    public override void Visit(SyntaxNode? Node) {
        if (Node == null) {
            return;
        }
        MethodScope Scope = MethodScopes.Peek();
        int Before = Scope.Complexity;
        if (IsDispatchCase(Node)) {
            Scope.Complexity++;
            Scope.DispatchCases++;
        } else if (Node is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or
            DoStatementSyntax or CatchClauseSyntax or ConditionalExpressionSyntax or WhenClauseSyntax) {
            Scope.Complexity++;
        } else if (Node is BinaryExpressionSyntax Binary &&
            (Binary.IsKind(SyntaxKind.LogicalAndExpression) || Binary.IsKind(SyntaxKind.LogicalOrExpression))) {
            Scope.Complexity++;
        }
        if (Scope.NestedCallableDepth > 0) {
            Scope.NestedCallableDecisions += Scope.Complexity - Before;
        }
        base.Visit(Node);
    }

    private static bool IsDispatchCase(SyntaxNode Node) {
        return Node is CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax ||
            Node is SwitchExpressionArmSyntax Arm && (Arm.WhenClause != null || Arm.Pattern is not (DiscardPatternSyntax or VarPatternSyntax));
    }

    private sealed class MethodScope {
        internal int Complexity { get; set; } = 1;
        internal int DispatchCases { get; set; }
        internal int NestedCallableDepth { get; set; }
        internal int NestedCallableDecisions { get; set; }
    }
}

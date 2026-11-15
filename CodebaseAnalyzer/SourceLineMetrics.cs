using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodebaseAnalyzer;

/// <summary>Counts physical source lines using Roslyn spans so strings and inline comments preserve code lines.</summary>
public static class SourceLineMetrics {
    /// <summary>
    /// Populates exclusive code, comment and blank counts. Code takes precedence on mixed lines;
    /// whitespace-only lines are blank, and a final newline does not create an extra physical line.
    /// </summary>
    public static void Populate(SyntaxTree Tree, FileStats Stats) {
        SourceText Source = Tree.GetText();
        int Count = Source.Lines.Count;
        if (Source.Lines[Count - 1].Span.IsEmpty) {
            Count--;
        }
        bool[] Code = new bool[Source.Lines.Count];
        bool[] Comments = new bool[Source.Lines.Count];
        SyntaxNode Root = Tree.GetRoot();
        foreach (SyntaxToken Token in Root.DescendantTokens()) {
            MarkLines(Token.Span, Code);
        }
        foreach (SyntaxTrivia Trivia in Root.DescendantTrivia()) {
            if (Trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || Trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                Trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || Trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)) {
                MarkLines(Trivia.Span, Comments);
            }
        }

        Stats.TotalLines = Count;
        Stats.CodeLines = Stats.CommentLines = Stats.BlankLines = 0;
        for (int Index = 0; Index < Count; Index++) {
            if (string.IsNullOrWhiteSpace(Source.Lines[Index].ToString())) {
                Stats.BlankLines++;
            } else if (!Code[Index] && Comments[Index]) {
                Stats.CommentLines++;
            } else {
                // Directives and disabled source also count as code in this source inventory.
                Stats.CodeLines++;
            }
        }

        void MarkLines(TextSpan Span, bool[] Destination) {
            if (Span.IsEmpty) {
                return;
            }
            int Start = Source.Lines.GetLineFromPosition(Span.Start).LineNumber;
            int End = Source.Lines.GetLineFromPosition(Span.End - 1).LineNumber;
            for (int Index = Start; Index <= End; Index++) {
                Destination[Index] = true;
            }
        }
    }
}

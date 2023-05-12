//#define DebugStuff

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using UnityEditor;
using USPPPatcher;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using VRC.SDK3.Data;


namespace PPList2DataList
{
    internal class SemanticModelWalker : CSharpSyntaxWalker
    {
        private List<ProgramOffset> _offsets = new List<ProgramOffset>();
        private SemanticModel _semanticModel;
        public string Program;

        public SemanticModelWalker(string program, SemanticModel semanticModel)
        {
            Program = program;
            _semanticModel = semanticModel;
        }
        
        private void AddOffset(int index, int offset)
        {
            var off = new ProgramOffset
            {
                Index = index,
                Offset = offset
            };
            _offsets.Add(off);
            _offsets = _offsets.OrderBy(x => x.Index).ToList();
        }

        private int GetOffset(int originalOffset)
        {
            var newOffset = originalOffset;
            foreach (var offset in _offsets)
            {
                if (offset.Index > originalOffset)
                    break;
                newOffset += offset.Offset;
            }

            return newOffset;
        }

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var currText = Program.Substring(GetOffset(node.Span.Start), node.Span.Length);

            if (_semanticModel.GetTypeInfo(node.Type).Type?.Name != "List")
            {
                base.VisitVariableDeclaration(node);
                return;
            }

            var rx = new Regex("List<[A-Za-z<> ,\\[\\]]+>");
            var newString = rx.Replace(currText, "DataList");
            
            var start = GetOffset(node.SpanStart);
            Program = Program.Remove(start, node.Span.Length)
                .Insert(start, newString);
                            
            AddOffset(node.Span.Start, newString.Length - currText.Length);
            
#if DebugStuff
            Debug.Log($"<color=#FF4040>VarDec</color> '{currText}'");
#endif
            base.VisitVariableDeclaration(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {

            var leftType = _semanticModel.GetTypeInfo(node.Left).Type;
            if (leftType == null || leftType.Name != "List")
            {
                base.VisitAssignmentExpression(node);
                return;
            }
            
            var currText = node.Right.GetText().ToString();
            if (!currText.StartsWith("new List"))
            {
                base.VisitAssignmentExpression(node);
                return;
            }
            
#if DebugStuff
            Debug.Log($"<color=#20FF40>Assignment</color> '{currText}, {node.Right.GetText()}'");
#endif
            var start = GetOffset(node.Right.Span.Start);
            var newAccess = "new DataList()";
                            
            Program = Program.Remove(start, node.Right.Span.Length)
                .Insert(start, newAccess);
                            
            AddOffset(node.Right.Span.Start, newAccess.Length - currText.Length);
            
            base.VisitAssignmentExpression(node);
        }

        private bool DataTokenType(ref string type)
        {
            var s = type;
            var thingy = Enum.GetNames(typeof(TokenType)).SingleOrDefault(x => x.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(thingy))
            {
                type = thingy;
                return false;
            }
            return true;
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            var currText = node.GetText();

            if (node.Expression is IdentifierNameSyntax identifierName)
            {
                var typeInfo = _semanticModel.GetTypeInfo(identifierName);
                if (typeInfo.Type is INamedTypeSymbol namedTypeSymbol)
                {
                    // check if it's a List
                    if (namedTypeSymbol.Name == "List" && namedTypeSymbol.IsGenericType)
                    {
                        // get the type argument(s) and format the full type name
                        var typeArgs = namedTypeSymbol.TypeArguments;
#if DebugStuff
                        Debug.Log($"<color=#000088>List variable</color> '{namedTypeSymbol.Name}' has type '<color=blue>{typeArgs.First().ToDisplayString()}</color>'");
#endif
                        var start = GetOffset(node.Span.Start);

                        // Replace the original List[x] with List[x].Type and some times ((Type)List[x].Reference) for unsupported values
                        var typeName = typeArgs.First().ToDisplayString();
                        var t = DataTokenType(ref typeName);
                        var newAccess = "";
                        if (!t)
                            newAccess = currText + "." + typeName;
                        else
                            newAccess = "((" + typeName + ")" + currText + ".Reference)";
                            
                        Program = Program.Remove(start, node.Span.Length)
                            .Insert(start, newAccess);
                            
                        AddOffset(node.Span.Start, newAccess.Length - currText.Length);
                    }
                }
            }

            base.VisitElementAccessExpression(node);
        }
    }
    
    internal class ProgramOffset
    {
        public int Index;
        public int Offset;
    }

    public static class PreProcessor
    {
        public static string[] Defines;
        
        private static string Parse(string program, PPInfo info)
        {
            var programSyntaxTree = CSharpSyntaxTree.ParseText(program, CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.None).WithPreprocessorSymbols(Defines).WithLanguageVersion(LanguageVersion.CSharp7_3));

            Compilation compilation = CSharpCompilation.Create("MyProgram", new[] { programSyntaxTree });

            var syntaxWalker = new SemanticModelWalker(program, compilation.GetSemanticModel(programSyntaxTree));
            syntaxWalker.Visit(programSyntaxTree.GetRoot());
#if DebugStuff
            Debug.Log("<color=red>Code:</color>\n\n"+syntaxWalker.Program);
#endif
            return syntaxWalker.Program;
        }
        
        private static string[] GetProjectDefines()
        {
            List<string> defines = new List<string>();

            foreach (string define in EditorUserBuildSettings.activeScriptCompilationDefines)
            {
                if (define.StartsWith("UNITY_EDITOR"))
                    continue;

                defines.Add(define);
            }

            defines.Add("COMPILER_UDONSHARP");

            return defines.ToArray();
        }
        
        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            Defines = GetProjectDefines();
            PPHandler.Subscribe(Parse, 1, "Example Text Replacer");
        }
    }
}

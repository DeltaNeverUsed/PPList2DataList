using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using UnityEditor;
using UnityEngine;
using USPPPatcher;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using USPPPatcher.Editor;

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
            
            Debug.Log($"New: {newString}, old: {currText}");

            var start = GetOffset(node.SpanStart);
            Program = Program.Remove(start, node.Span.Length)
                .Insert(start, newString);
                            
            AddOffset(node.Span.Start, newString.Length - currText.Length);
            
            Debug.Log($"<color=#FF4040>VarDec</color> '{currText}'");
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
            
            Debug.Log($"<color=#20FF40>Assignment</color> '{currText}, {node.Right.GetText()}'");
            
            var start = GetOffset(node.Right.Span.Start);
            var newAccess = "new DataList()";
                            
            Program = Program.Remove(start, node.Right.Span.Length)
                .Insert(start, newAccess);
                            
            AddOffset(node.Right.Span.Start, newAccess.Length - currText.Length);
            
            base.VisitAssignmentExpression(node);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            var currText = node.GetText();
            Debug.Log($"<color=#008040>ElementAccess</color> '{currText}'");
            

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
                        Debug.Log(
                            $"<color=#000088>List variable</color> '{namedTypeSymbol.Name}' has type '<color=blue>{typeArgs.First().Name}</color>'");

                        var start = GetOffset(node.Span.Start);
                        var newAccess = currText + "." + typeArgs.First().Name;
                            
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
            if (!program.Contains("MEEE!!!") || program.Contains("string Parse("))
                return program;
            
            
            var programSyntaxTree = CSharpSyntaxTree.ParseText(program, CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.None).WithPreprocessorSymbols(Defines).WithLanguageVersion(LanguageVersion.CSharp7_3));

            Compilation compilation = CSharpCompilation.Create("MyProgram", new[] { programSyntaxTree });

            var syntaxWalker = new SemanticModelWalker(program, compilation.GetSemanticModel(programSyntaxTree));
            syntaxWalker.Visit(programSyntaxTree.GetRoot());
            
            Debug.Log("<color=red>Code:</color>\n\n"+syntaxWalker.Program);
            

            /*
            var t = info.Analyzer.GetVariablesInSpaceByType(@"\bList<([\w]*)>");

            foreach (var list in t)
            {
                var match = Regex.Match(list.Type, @"^(List)<([A-Za-z0-9_\.]+)>$");
            
                var subType = match.Groups[2].Value;

                var defLen = program.IndexOf(' ', list.Index) - list.Index;
                modified = modified.Remove(list.Index, defLen);
                modified = modified.Insert(list.Index, $"DataList");
                info.Analyzer.OffsetEverything(list.Index, defLen, "DataList");

                foreach (var use in list.Uses)
                {
                    var end = modified.IndexOf(';', use);
                    var subStr = modified.Substring(use, end - use);
                    if (subStr.Contains("new "))
                    {
                        subStr = Regex.Replace(subStr, @"List<([A-Za-z0-9_\.]+)>", "DataList");
                        modified = modified.Remove(use, end - use).Insert(use, subStr);
                    }
                    else if (subStr.Contains('='))
                    {
                        
                    }
                    else
                    {
                        var re = @"\b"+list.Name+@"(?!\s*=)(?!\s*\.)\b";
                        var s = Regex.Match(subStr, re);
                        re = @"\[(?:[^\[\]]*(?:\[(?<Depth>)|\](?<-Depth>))*(?(Depth)(?!)))?\]";
                        var d = Regex.IsMatch(subStr, re);
                        Debug.Log("A_ "+s.Value + " d_ " + d);
                    }
                }
                
                Debug.Log(ObjectDumper.Dump(list));
            }
            
            Debug.Log(modified);*/
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
            PPHandler.Subscribe(Parse, 1, "Example Text Replacer", true);
        }
    }
}

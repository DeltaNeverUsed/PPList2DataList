using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using USPPPatcher;

namespace PPList2DataList
{
    public static class PreProcessor
    {
        private static string Parse(string program, PPInfo info)
        {
            if (!program.Contains("MEEE!!!") || program.Contains("string Parse("))
                return program;
            var modified = program;

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
            
            Debug.Log(modified);
            return program;
        }
        
        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            PPHandler.Subscribe(Parse, 1, "Example Text Replacer", true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Gizbox.IR;

namespace Gizbox
{
    public class LexerExtender
    {
        private Compiler compiler;

        public LexerExtender(Compiler compiler)
        {
            this.compiler = compiler;
        }

        /// <summary> 收集源码及依赖库中的已知类型名，供语言服务和扫描器识别新类型 </summary>
        public HashSet<string> CollectTypeNames(string source)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            foreach(var name in CollectTypeNamesFromSource(source))
            {
                AddTypeName(result, name);
            }

            foreach(var libName in CollectImportNames(source))
            {
                var lib = compiler.LoadLib(libName);
                lib.AutoLoadDependencies(compiler);
                CollectTypeNamesFromLib(lib, result);
            }

            return result;
        }

        private IEnumerable<string> CollectTypeNamesFromSource(string source)
        {
            if(string.IsNullOrEmpty(source))
                yield break;

            var classRegex = new Regex(@"\bclass\s+(?:own\s+)?([A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)*)");
            foreach(Match match in classRegex.Matches(source))
            {
                if(match.Success)
                    yield return match.Groups[1].Value;
            }

            var structRegex = new Regex(@"\bstruct\s+([A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)*)");
            foreach(Match match in structRegex.Matches(source))
            {
                if(match.Success)
                    yield return match.Groups[1].Value;
            }

            var enumRegex = new Regex(@"\benum\s+([A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)*)");
            foreach(Match match in enumRegex.Matches(source))
            {
                if(match.Success)
                    yield return match.Groups[1].Value;
            }

            var classGenericRegex = new Regex(@"\bclass\s+(?:own\s+)?[A-Za-z_][A-Za-z0-9_]*\s*<([^>]+)>");
            foreach(Match match in classGenericRegex.Matches(source))
            {
                if(!match.Success)
                    continue;

                foreach(var name in SplitGenericParams(match.Groups[1].Value))
                {
                    yield return name;
                }
            }

            var structGenericRegex = new Regex(@"\bstruct\s+[A-Za-z_][A-Za-z0-9_]*\s*<([^>]+)>");
            foreach(Match match in structGenericRegex.Matches(source))
            {
                if(!match.Success)
                    continue;

                foreach(var name in SplitGenericParams(match.Groups[1].Value))
                {
                    yield return name;
                }
            }

            var funcGenericRegex = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\s*<([^>]+)>\s*\(");
            foreach(Match match in funcGenericRegex.Matches(source))
            {
                if(!match.Success)
                    continue;

                foreach(var name in SplitGenericParams(match.Groups[1].Value))
                {
                    yield return name;
                }
            }
        }

        private static IEnumerable<string> SplitGenericParams(string value)
        {
            return value.Split(',')
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v));
        }

        private static IEnumerable<string> CollectImportNames(string source)
        {
            if(string.IsNullOrEmpty(source))
                yield break;

            var importRegex = new Regex(@"\bimport\s*<\s*""([^""]+)""\s*>");
            foreach(Match match in importRegex.Matches(source))
            {
                if(match.Success)
                    yield return match.Groups[1].Value;
            }
        }

        private void CollectTypeNamesFromLib(IRUnit lib, HashSet<string> result)
        {
            if(lib == null)
                return;

            foreach(var templateClass in lib.templateClasses)
            {
                AddTypeName(result, templateClass);
            }

            foreach(var templateStruct in lib.templateStructs)
            {
                AddTypeName(result, templateStruct);
            }

            foreach(var record in lib.globalScope.env.records.Values)
            {
                if(record.category != SymbolTable.RecordCatagory.Class
                    && record.category != SymbolTable.RecordCatagory.Struct
                    && record.category != SymbolTable.RecordCatagory.Enum)
                    continue;

                var name = string.IsNullOrWhiteSpace(record.rawname) ? record.name : record.rawname;
                AddTypeName(result, name);
                AddTypeName(result, record.name);
            }

            if(lib.dependencyLibs == null)
                return;

            foreach(var dep in lib.dependencyLibs)
            {
                CollectTypeNamesFromLib(dep, result);
            }
        }

        private static void AddTypeName(HashSet<string> result, string name)
        {
            if(string.IsNullOrWhiteSpace(name))
                return;

            result.Add(name);
            var idx = name.LastIndexOf("::", StringComparison.Ordinal);
            if(idx >= 0 && idx + 2 < name.Length)
            {
                result.Add(name.Substring(idx + 2));
            }
        }

    }
}
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;
using System.Linq;
using System.Management.Automation.Language;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// AlignAssignmentStatement: Checks if consecutive assignment statements are aligned.
    /// </summary>
#if !CORECLR
    [Export(typeof(IScriptRule))]
#endif
    public class AlignAssignmentStatement : ConfigurableRule
    {
        // We keep this switch even though the rule has only one switch (this) as of now, because we want
        // to let the rule be expandable in the future to allow formatting assignments even
        // in variable assignments. But for now we will stick to only one option.
        /// <summary>
        /// Check if key value pairs in a hashtable are aligned or not.
        /// </summary>
        /// <returns></returns>
        [ConfigurableRuleProperty(defaultValue: true)]
        public bool CheckHashtable { get; set; }

        private readonly char whitespaceChar = ' ';

        private List<Func<TokenOperations, IEnumerable<DiagnosticRecord>>> violationFinders
            = new List<Func<TokenOperations, IEnumerable<DiagnosticRecord>>>();

        /// <summary>
        /// Sets the configurable properties of this rule.
        /// </summary>
        /// <param name="paramValueMap">A dictionary that maps parameter name to it value. Must be non-null</param>
        public override void ConfigureRule(IDictionary<string, object> paramValueMap)
        {
            base.ConfigureRule(paramValueMap);
            if (CheckHashtable)
            {
                violationFinders.Add(FindHashtableViolations);
            }
        }


        /// <summary>
        /// Analyzes the given ast to find if consecutive assignment statements are aligned.
        /// </summary>
        /// <param name="ast">AST to be analyzed. This should be non-null</param>
        /// <param name="fileName">Name of file that corresponds to the input AST.</param>
        /// <returns>A an enumerable type containing the violations</returns>
        public override IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null)
            {
                throw new ArgumentNullException("ast");
            }
            // only handles one line assignments
            // if the rule encounters assignment statements that are multi-line, the rule will ignore that block

            var tokenOps = new TokenOperations(Helper.Instance.Tokens, ast);
            foreach (var violationFinder in violationFinders)
            {
                foreach (var diagnosticRecord in violationFinder(tokenOps))
                {
                    yield return diagnosticRecord;
                }
            }
        }

        /// <summary>
        /// Retrieves the common name of this rule.
        /// </summary>
        public override string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AlignAssignmentStatementCommonName);
        }

        /// <summary>
        /// Retrieves the description of this rule.
        /// </summary>
        public override string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AlignAssignmentStatementDescription);
        }

        /// <summary>
        /// Retrieves the name of this rule.
        /// </summary>
        public override string GetName()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.NameSpaceFormat,
                GetSourceName(),
                Strings.AlignAssignmentStatementName);
        }

        /// <summary>
        /// Retrieves the severity of the rule: error, warning or information.
        /// </summary>
        public override RuleSeverity GetSeverity()
        {
            return RuleSeverity.Warning;
        }

        /// <summary>
        /// Gets the severity of the returned diagnostic record: error, warning, or information.
        /// </summary>
        /// <returns></returns>
        public DiagnosticSeverity GetDiagnosticSeverity()
        {
            return DiagnosticSeverity.Warning;
        }

        /// <summary>
        /// Retrieves the name of the module/assembly the rule is from.
        /// </summary>
        public override string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }

        /// <summary>
        /// Retrieves the type of the rule, Builtin, Managed or Module.
        /// </summary>
        public override SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }
        private IEnumerable<DiagnosticRecord> FindHashtableViolations(TokenOperations tokenOps)
        {
            var hashtableAsts = tokenOps.Ast.FindAll(ast => ast is HashtableAst, true);
            var groups = new List<List<Tuple<IScriptExtent, IScriptExtent>>>();
            if (hashtableAsts != null)
            {
                foreach (var astItem in hashtableAsts)
                {
                    groups.Add(GetExtents(tokenOps, (HashtableAst)astItem));
                }
            }

#if !PSV3
            var configAsts = tokenOps.Ast.FindAll(ast => ast is ConfigurationDefinitionAst, true);
            if (configAsts != null)
            {
                // There are probably parse errors caused by an "Undefined DSC resource"
                // which prevents the parser from detecting the property value pairs as
                // hashtable. Hence, this is a workaround to format configurations which
                // have "Undefined DSC resource" parse errors.

                // find all commandAsts of the form "prop" "=" "val" that have the same parent
                // and format those pairs.
                foreach (var configAst in configAsts)
                {
                    groups.AddRange(GetCommandElementExtentGroups(configAst));
                }
            }
#endif

            // it is probably much easier have a hashtable writer that formats the hashtable and writes it
            // but it makes handling comments hard. So we need to use this approach.

            // This is how the algorithm actually works:
            // if each key value pair are on a separate line
            //   find all the assignment operators
            //   if all the assignment operators are aligned (check the column number of each assignment operator)
            //      skip
            //   else
            //      find the distance between the assignment operators and their corresponding LHS
            //      find the longest left expression
            //      make sure all the assignment operators are in the same column as that of the longest left hand.
            foreach (var extentTuples in groups)
            {
                if (!HasPropertiesOnSeparateLines(extentTuples))
                {
                    continue;
                }

                if (extentTuples == null
                    || extentTuples.Count == 0
                    || !extentTuples.All(t => t.Item1.StartLineNumber == t.Item2.EndLineNumber))
                {
                    continue;
                }

                var expectedStartColumnNumber = extentTuples.Max(x => x.Item1.EndColumnNumber) + 1;
                foreach (var extentTuple in extentTuples)
                {
                    if (extentTuple.Item2.StartColumnNumber != expectedStartColumnNumber)
                    {
                        yield return new DiagnosticRecord(
                            GetError(),
                            extentTuple.Item2,
                            GetName(),
                            GetDiagnosticSeverity(),
                            extentTuple.Item1.File,
                            null,
                            GetHashtableCorrections(extentTuple, expectedStartColumnNumber).ToList());
                    }
                }
            }
        }

        private List<List<Tuple<IScriptExtent, IScriptExtent>>> GetCommandElementExtentGroups(Ast configAst)
        {
            var result = new List<List<Tuple<IScriptExtent, IScriptExtent>>>();
            var commandAstGroups = GetCommandElementGroups(configAst);
            foreach (var commandAstGroup in commandAstGroups)
            {
                var list = new List<Tuple<IScriptExtent, IScriptExtent>>();
                foreach (var commandAst in commandAstGroup)
                {
                    var elems = commandAst.CommandElements;
                    list.Add(new Tuple<IScriptExtent, IScriptExtent>(elems[0].Extent, elems[1].Extent));
                }

                result.Add(list);
            }

            return result;
        }

        private List<List<CommandAst>> GetCommandElementGroups(Ast configAst)
        {
            var result = new List<List<CommandAst>>();
            var astsFound = configAst.FindAll(ast => IsPropertyValueCommandAst(ast), true);
            if (astsFound == null)
            {
                return result;
            }

            var parentChildrenGroup = from ast in astsFound
                                      select (CommandAst)ast into commandAst
                                      group commandAst by commandAst.Parent.Parent; // parent is pipeline and pipeline's parent is namedblockast
            foreach (var group in parentChildrenGroup)
            {
                result.Add(group.ToList());
            }

            return result;
        }

        private bool IsPropertyValueCommandAst(Ast ast)
        {
            var commandAst = ast as CommandAst;
            return commandAst != null
                && commandAst.CommandElements.Count() == 3
                && commandAst.CommandElements[1].Extent.Text.Equals("=");
        }

        private IEnumerable<CorrectionExtent> GetHashtableCorrections(
            Tuple<IScriptExtent, IScriptExtent> extentTuple,
            int expectedStartColumnNumber)
        {
            var equalExtent = extentTuple.Item2;
            var lhsExtent = extentTuple.Item1;
            var columnDiff = expectedStartColumnNumber - equalExtent.StartColumnNumber;
            yield return new CorrectionExtent(
                lhsExtent.EndLineNumber,
                equalExtent.StartLineNumber,
                lhsExtent.EndColumnNumber,
                equalExtent.StartColumnNumber,
                new String(whitespaceChar, expectedStartColumnNumber - lhsExtent.EndColumnNumber),
                GetError());
        }

        private string GetError()
        {
            return String.Format(CultureInfo.CurrentCulture, Strings.AlignAssignmentStatementError);
        }

        private static List<Tuple<IScriptExtent, IScriptExtent>> GetExtents(
            TokenOperations tokenOps,
            HashtableAst hashtableAst)
        {
            var nodeTuples = new List<Tuple<IScriptExtent, IScriptExtent>>();
            foreach (var kvp in hashtableAst.KeyValuePairs)
            {
                var keyStartOffset = kvp.Item1.Extent.StartOffset;
                bool keyStartOffSetReached = false;
                var keyTokenNode = tokenOps.GetTokenNodes(
                    token =>
                    {
                        if (keyStartOffSetReached)
                        {
                            return token.Kind == TokenKind.Equals;
                        }
                        if (token.Extent.StartOffset == keyStartOffset)
                        {
                            keyStartOffSetReached = true;
                        }
                        return false;
                        }).FirstOrDefault();
                if (keyTokenNode == null || keyTokenNode.Value == null)
                {
                    continue;
                }
                var assignmentToken = keyTokenNode.Value.Extent;

                nodeTuples.Add(new Tuple<IScriptExtent, IScriptExtent>(
                    kvp.Item1.Extent, assignmentToken));
            }

            return nodeTuples;
        }

        private bool HasPropertiesOnSeparateLines(IEnumerable<Tuple<IScriptExtent, IScriptExtent>> tuples)
        {
            if (tuples.Count() == 1)
            {
                // If the hashtable has just a single key-value pair, it does not have properties on separate lines
                return false;
            }
            var lines = new HashSet<int>();
            foreach (var kvp in tuples)
            {
                if (lines.Contains(kvp.Item1.StartLineNumber))
                {
                    return false;
                }
                else
                {
                    lines.Add(kvp.Item1.StartLineNumber);
                }
            }

            return true;
        }
    }
}

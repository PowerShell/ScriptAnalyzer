// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;
using System.Management.Automation.Language;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// AvoidTrailingWhitespace: Checks for trailing whitespaces
    /// </summary>
#if !CORECLR
    [Export(typeof(IScriptRule))]
#endif
    public class AvoidTrailingWhitespace : IScriptRule
    {
        /// <summary>
        /// Analyzes the given ast to find violations.
        /// </summary>
        /// <param name="ast">AST to be analyzed. This should be non-null</param>
        /// <param name="fileName">Name of file that corresponds to the input AST.</param>
        /// <returns>A an enumerable type containing the violations</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null)
            {
                throw new ArgumentNullException("ast");
            }

            var diagnosticRecords = new List<DiagnosticRecord>();

            string[] lines = ast.Extent.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            {
                var line = lines[lineNumber];

                if (line.Length == 0)
                {
                    continue;
                }

                if (!char.IsWhiteSpace(line[line.Length - 1]) &&
                    line[line.Length - 1] != '\t')
                {
                    continue;
                }

                int startColumnOfTrailingWhitespace = 1;
                for (int i = line.Length - 2; i >= 0; i--)
                {
                    if (line[i] != ' ' && line[i] != '\t')
                    {
                        startColumnOfTrailingWhitespace = i + 2;
                        break;
                    }
                }

                int startLine = lineNumber + 1;
                int endLine = startLine;
                int startColumn = startColumnOfTrailingWhitespace;
                int endColumn = line.Length + 1;

                var violationExtent = new ScriptExtent(
                    new ScriptPosition(
                        ast.Extent.File,
                        startLine,
                        startColumn,
                        line
                    ),
                    new ScriptPosition(
                        ast.Extent.File,
                        endLine,
                        endColumn,
                        line
                    ));

                var suggestedCorrections = new List<CorrectionExtent>();
                suggestedCorrections.Add(new CorrectionExtent(
                            violationExtent,
                            string.Empty,
                            ast.Extent.File
                        ));

                diagnosticRecords.Add(
                    new DiagnosticRecord(
                        String.Format(CultureInfo.CurrentCulture, Strings.AvoidTrailingWhitespaceError),
                        violationExtent,
                        GetName(),
                        GetDiagnosticSeverity(),
                        ast.Extent.File,
                        null,
                        suggestedCorrections
                    ));
            }

            return diagnosticRecords;
        }

        /// <summary>
        /// Retrieves the common name of this rule.
        /// </summary>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidTrailingWhitespaceCommonName);
        }

        /// <summary>
        /// Retrieves the description of this rule.
        /// </summary>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidTrailingWhitespaceDescription);
        }

        /// <summary>
        /// Retrieves the name of this rule.
        /// </summary>
        public string GetName()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.NameSpaceFormat,
                GetSourceName(),
                Strings.AvoidTrailingWhitespaceName);
        }

        /// <summary>
        /// Retrieves the severity of the rule: error, warning or information.
        /// </summary>
        public RuleSeverity GetSeverity()
        {
            return RuleSeverity.Information;
        }

        /// <summary>
        /// Gets the severity of the returned diagnostic record: error, warning, or information.
        /// </summary>
        /// <returns></returns>
        public DiagnosticSeverity GetDiagnosticSeverity()
        {
            return DiagnosticSeverity.Information;
        }

        /// <summary>
        /// Retrieves the name of the module/assembly the rule is from.
        /// </summary>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }

        /// <summary>
        /// Retrieves the type of the rule, Builtin, Managed or Module.
        /// </summary>
        public SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }
    }
}

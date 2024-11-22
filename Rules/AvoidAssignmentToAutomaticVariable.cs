// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Collections.Generic;
using System.Management.Automation.Language;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// AvoidAssignmentToAutomaticVariable: Checks for assignment to automatic variables.
    /// </summary>
#if !CORECLR
[Export(typeof(IScriptRule))]
#endif
    public class AvoidAssignmentToAutomaticVariable : IScriptRule
    {
        private static readonly IList<string> _readOnlyAutomaticVariables = new List<string>()
        {
            // Attempting to assign to any of those read-only variable would result in an error at runtime
            "?", "true", "false", "Host", "PSCulture", "Error", "ExecutionContext", "Home", "PID", "PSEdition", "PSHome", "PSUICulture", "PSVersionTable", "ShellId"
        };

        private static readonly IList<string> _readOnlyAutomaticVariablesIntroducedInVersion6_0 = new List<string>()
        {
            // Attempting to assign to any of those read-only variable will result in an error at runtime
            "IsCoreCLR", "IsLinux", "IsMacOS", "IsWindows"
        };

        private static readonly IReadOnlyList<string> _writableAutomaticVariables = new List<string>()
        {
            // Attempting to assign to any of those could cause issues, only in some special cases could assignment be by design
            "_", "AllNodes", "Args", "ConsoleFilename", "Event", "EventArgs", "EventSubscriber", "ForEach", "Input", "Matches", "MyInvocation",
            "NestedPromptLevel", "Profile", "PSBoundParameters", "PsCmdlet", "PSCommandPath", "PSDebugContext",
            "PSItem", "PSScriptRoot", "PSSenderInfo", "Pwd", "PSCommandPath", "ReportErrorShowExceptionClass",
            "ReportErrorShowInnerException", "ReportErrorShowSource", "ReportErrorShowStackTrace", "Sender",
            "StackTrace", "This"
        };

        /// <summary>
        /// Checks for assignment to automatic variables.
        /// <param name="ast">The script's ast</param>
        /// <param name="fileName">The script's file name</param>
        /// <returns>The diagnostic results of this rule</returns>
        /// </summary>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            IEnumerable<Ast> assignmentStatementAsts = ast.FindAll(testAst => testAst is AssignmentStatementAst, searchNestedScriptBlocks: true);
            foreach (AssignmentStatementAst assignmentStatementAst in assignmentStatementAsts)
            {
                var variableExpressionAst = assignmentStatementAst.Left.Find(testAst => testAst is VariableExpressionAst && testAst.Parent == assignmentStatementAst, searchNestedScriptBlocks: false) as VariableExpressionAst;
                if (variableExpressionAst == null) { continue; }
                var variableName = variableExpressionAst.VariablePath.UserPath;
                if (_readOnlyAutomaticVariables.Contains(variableName, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new DiagnosticRecord(DiagnosticRecordHelper.FormatError(Strings.AvoidAssignmentToReadOnlyAutomaticVariableError, variableName),
                                                      variableExpressionAst.Extent, GetName(), DiagnosticSeverity.Error, fileName, variableName);
                }

                if (_readOnlyAutomaticVariablesIntroducedInVersion6_0.Contains(variableName, StringComparer.OrdinalIgnoreCase))
                {
                    var severity = IsPowerShellVersion6OrGreater() ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                    yield return new DiagnosticRecord(DiagnosticRecordHelper.FormatError(Strings.AvoidAssignmentToReadOnlyAutomaticVariableIntroducedInPowerShell6_0Error, variableName),
                                                      variableExpressionAst.Extent, GetName(), severity, fileName, variableName);
                }

                if (_writableAutomaticVariables.Contains(variableName, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new DiagnosticRecord(DiagnosticRecordHelper.FormatError(Strings.AvoidAssignmentToWritableAutomaticVariableError, variableName),
                                                      variableExpressionAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName, variableName);
                }
            }

            IEnumerable<Ast> parameterAsts = ast.FindAll(testAst => testAst is ParameterAst, searchNestedScriptBlocks: true);
            foreach (ParameterAst parameterAst in parameterAsts)
            {
                var variableExpressionAst = parameterAst.Find(testAst => testAst is VariableExpressionAst, searchNestedScriptBlocks: false) as VariableExpressionAst;
                var variableName = variableExpressionAst.VariablePath.UserPath;
                // also check the parent to exclude parameter attributes such as '[Parameter(Mandatory=$true)]' where the read-only variable $true appears.
                if (variableExpressionAst.Parent is NamedAttributeArgumentAst)
                {
                    continue;
                }
                // also check the parent to exclude variableExpressions that appear within attributes,
                // such as '[ValidateSet($True,$False)]' where the read-only variables $true,$false appear.
                if (variableExpressionAst.Parent is AttributeAst)
                {
                    continue;
                }
                if (_readOnlyAutomaticVariables.Contains(variableName, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new DiagnosticRecord(DiagnosticRecordHelper.FormatError(Strings.AvoidAssignmentToReadOnlyAutomaticVariableError, variableName),
                                                      variableExpressionAst.Extent, GetName(), DiagnosticSeverity.Error, fileName, variableName);
                }

                if (_readOnlyAutomaticVariablesIntroducedInVersion6_0.Contains(variableName, StringComparer.OrdinalIgnoreCase))
                {
                    var severity = IsPowerShellVersion6OrGreater() ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                    yield return new DiagnosticRecord(DiagnosticRecordHelper.FormatError(Strings.AvoidAssignmentToReadOnlyAutomaticVariableIntroducedInPowerShell6_0Error, variableName),
                                                      variableExpressionAst.Extent, GetName(), severity, fileName, variableName);
                }

                if (_writableAutomaticVariables.Contains(variableName, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new DiagnosticRecord(DiagnosticRecordHelper.FormatError(Strings.AvoidAssignmentToWritableAutomaticVariableError, variableName),
                                                      variableExpressionAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName, variableName);
                }
            }
        }

        private bool IsPowerShellVersion6OrGreater()
        {
            var psVersion = Helper.Instance.GetPSVersion();
            if (psVersion.Major >= 6)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.AvoidAssignmentToAutomaticVariableName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidAssignmentToReadOnlyAutomaticVariableCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidAssignmentToReadOnlyAutomaticVariableDescription);
        }

        /// <summary>
        /// GetSourceType: Retrieves the type of the rule: builtin, managed or module.
        /// </summary>
        public SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }

        /// <summary>
        /// GetSeverity: Retrieves the severity of the rule: error, warning of information.
        /// </summary>
        /// <returns></returns>
        public RuleSeverity GetSeverity()
        {
            return RuleSeverity.Warning;
        }

        /// <summary>
        /// GetSourceName: Retrieves the module/assembly name the rule is from.
        /// </summary>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }
    }

}

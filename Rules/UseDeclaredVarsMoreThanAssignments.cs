// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Management.Automation.Language;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
using System.Linq;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// UseDeclaredVarsMoreThanAssignments: Analyzes the ast to check that variables are used in more than just their assignment.
    /// </summary>
#if !CORECLR
[Export(typeof(IScriptRule))]
#endif
    public class UseDeclaredVarsMoreThanAssignments : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Analyzes the ast to check that variables are used in more than just there assignment.
        /// </summary>
        /// <param name="ast">The script's ast</param>
        /// <param name="fileName">The script's file name</param>
        /// <returns>A List of results from this rule</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null)
            {
                throw new ArgumentNullException(Strings.NullAstErrorMessage);
            }

            var scriptBlockAsts = ast.FindAll(x => x is ScriptBlockAst, true);
            if (scriptBlockAsts == null)
            {
                yield break;
            }

            foreach (var scriptBlockAst in scriptBlockAsts)
            {
                var sbAst = scriptBlockAst as ScriptBlockAst;
                foreach (var diagnosticRecord in AnalyzeScriptBlockAst(sbAst, fileName))
                {
                    yield return diagnosticRecord;
                }
            }
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.UseDeclaredVarsMoreThanAssignmentsName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseDeclaredVarsMoreThanAssignmentsCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseDeclaredVarsMoreThanAssignmentsDescription);
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

        /// <summary>
        /// Checks if a variable is initialized and referenced in either its assignment or children scopes
        /// </summary>
        /// <param name="scriptBlockAst">Ast of type ScriptBlock</param>
        /// <param name="fileName">Name of file containing the ast</param>
        /// <returns>An enumerable containing diagnostic records</returns>
        private IEnumerable<DiagnosticRecord> AnalyzeScriptBlockAst(ScriptBlockAst scriptBlockAst, string fileName)
        {
            IEnumerable<Ast> assignmentAsts = scriptBlockAst.FindAll(testAst => testAst is AssignmentStatementAst, false);
            IEnumerable<Ast> varAsts = scriptBlockAst.FindAll(testAst => testAst is VariableExpressionAst, true);
            IEnumerable<Ast> varsInAssignment;

            Dictionary<string, AssignmentStatementAst> assignmentsDictionary_OrdinalIgnoreCase = new Dictionary<string, AssignmentStatementAst>(StringComparer.OrdinalIgnoreCase);

            string varKey;
            bool inAssignment;

            if (assignmentAsts == null)
            {
                yield break;
            }

            foreach (AssignmentStatementAst assignmentAst in assignmentAsts)
            {
                // Only checks for the case where lhs is a variable. Ignore things like $foo.property
                VariableExpressionAst assignmentVarAst = assignmentAst.Left as VariableExpressionAst;

                if (assignmentVarAst == null)
                {
                    // If the variable is declared in a strongly typed way, e.g. [string]$s = 'foo' then the type is ConvertExpressionAst.
                    // Therefore we need to the VariableExpressionAst from its Child property.
                    var assignmentVarAstAsConvertExpressionAst = assignmentAst.Left as ConvertExpressionAst;
                    if (assignmentVarAstAsConvertExpressionAst != null && assignmentVarAstAsConvertExpressionAst.Child != null)
                    {
                        assignmentVarAst = assignmentVarAstAsConvertExpressionAst.Child as VariableExpressionAst;
                    }
                }

                if (assignmentVarAst != null)
                {
                    // Ignore if variable is global or environment variable or scope is drive qualified variable
                    if (!Helper.Instance.IsVariableGlobalOrEnvironment(assignmentVarAst)
                        && !assignmentVarAst.VariablePath.IsScript
                        && assignmentVarAst.VariablePath.DriveName == null)
                    {
                        string variableName = Helper.Instance.VariableNameWithoutScope(assignmentVarAst.VariablePath);

                        if (!assignmentsDictionary_OrdinalIgnoreCase.ContainsKey(variableName))
                        {
                            assignmentsDictionary_OrdinalIgnoreCase.Add(variableName, assignmentAst);
                        }
                    }
                }
            }

            if (varAsts != null)
            {
                foreach (VariableExpressionAst varAst in varAsts)
                {
                    varKey = Helper.Instance.VariableNameWithoutScope(varAst.VariablePath);
                    inAssignment = false;

                    if (assignmentsDictionary_OrdinalIgnoreCase.ContainsKey(varKey))
                    {
                        varsInAssignment = assignmentsDictionary_OrdinalIgnoreCase[varKey].Left.FindAll(testAst => testAst is VariableExpressionAst, true);

                        // Checks if this variableAst is part of the logged assignment
                        foreach (VariableExpressionAst varInAssignment in varsInAssignment)
                        {
                            // Try casting to AssignmentStatementAst to be able to catch case where a variable is assigned more than once (https://github.com/PowerShell/PSScriptAnalyzer/issues/833)
                            var varInAssignmentAsStatementAst = varInAssignment.Parent as AssignmentStatementAst;
                            var varAstAsAssignmentStatementAst = varAst.Parent as AssignmentStatementAst;
                            if (varAstAsAssignmentStatementAst != null)
                            {
                                if (varAstAsAssignmentStatementAst.Operator == TokenKind.Equals)
                                {
                                    if (varInAssignmentAsStatementAst != null)
                                    {
                                        inAssignment = varInAssignmentAsStatementAst.Left.Extent.Text.Equals(varAstAsAssignmentStatementAst.Left.Extent.Text, StringComparison.OrdinalIgnoreCase);
                                    }
                                    else
                                    {
                                        inAssignment = varInAssignment.Equals(varAst);
                                    }
                                }
                            }
                            else
                            {
                                inAssignment = varInAssignment.Equals(varAst);
                            }
                        }

                        if (!inAssignment)
                        {
                            assignmentsDictionary_OrdinalIgnoreCase.Remove(varKey);
                        }

                        // Check if variable belongs to PowerShell built-in variables
                        if (Helper.Instance.HasSpecialVars(varKey))
                        {
                            assignmentsDictionary_OrdinalIgnoreCase.Remove(varKey);
                        }
                    }
                }
            }

            AnalyzeGetVariableCommands(scriptBlockAst, assignmentsDictionary_OrdinalIgnoreCase);

            foreach (string key in assignmentsDictionary_OrdinalIgnoreCase.Keys)
            {
                yield return new DiagnosticRecord(
                    string.Format(CultureInfo.CurrentCulture, Strings.UseDeclaredVarsMoreThanAssignmentsError, key),
                    assignmentsDictionary_OrdinalIgnoreCase[key].Left.Extent,
                    GetName(),
                    DiagnosticSeverity.Warning,
                    fileName,
                    key);
            }
        }

        /// <summary>
        /// Detects variables retrieved by usage of Get-Variable and remove those
        /// variables from the entries in <paramref name="assignmentsDictionary_OrdinalIgnoreCase"/>.
        /// </summary>
        /// <param name="scriptBlockAst"></param>
        /// <param name="assignmentsDictionary_OrdinalIgnoreCase"></param>
        private void AnalyzeGetVariableCommands(
            ScriptBlockAst scriptBlockAst,
            Dictionary<string, AssignmentStatementAst> assignmentsDictionary_OrdinalIgnoreCase)
        {
            var getVariableCmdletNamesAndAliases = Helper.Instance.CmdletNameAndAliases("Get-Variable");
            IEnumerable<Ast> getVariableCommandAsts = scriptBlockAst.FindAll(testAst => testAst is CommandAst commandAst &&
                getVariableCmdletNamesAndAliases.Contains(commandAst.GetCommandName(), StringComparer.OrdinalIgnoreCase), true);

            foreach (CommandAst getVariableCommandAst in getVariableCommandAsts)
            {
                var commandElements = getVariableCommandAst.CommandElements.ToList();
                // The following extracts the variable name(s) only in the simplest possible usage of Get-Variable.
                // Usage of a named parameter and an array of variables is accounted for though.
                if (commandElements.Count < 2 || commandElements.Count > 3) { continue; }

                var commandElementAstOfVariableName = commandElements[commandElements.Count - 1];
                if (commandElements.Count == 3)
                {
                    if (!(commandElements[1] is CommandParameterAst commandParameterAst)) { continue; }
                    // Check if the named parameter -Name is used (PowerShell does not need the full
                    // parameter name and there is no other parameter of Get-Variable starting with n).
                    if (!commandParameterAst.ParameterName.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (commandElementAstOfVariableName is StringConstantExpressionAst constantExpressionAst)
                {
                    assignmentsDictionary_OrdinalIgnoreCase.Remove(constantExpressionAst.Value);
                    continue;
                }

                if (!(commandElementAstOfVariableName is ArrayLiteralAst arrayLiteralAst)) { continue; }
                foreach (var expressionAst in arrayLiteralAst.Elements)
                {
                    if (expressionAst is StringConstantExpressionAst constantExpressionAstOfArray)
                    {
                        assignmentsDictionary_OrdinalIgnoreCase.Remove(constantExpressionAstOfArray.Value);
                    }
                }
            }
        }
    }
}

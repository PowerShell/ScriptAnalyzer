﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
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
    /// AvoidUnInitializedVarsInNewRunspaces: Analyzes the ast to check that variables in script blocks that run in new run spaces are properly initialized or passed in with '$using:(varName)'.
    /// </summary>
#if !CORECLR
[Export(typeof(IScriptRule))]
#endif
    public class AvoidUnInitializedVarsInNewRunspaces : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Analyzes the ast to check variables in script blocks that will run in new runspaces are properly initialized or passed in with $using:
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
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(),
                Strings.AvoidUnInitializedVarsInNewRunspacesName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidUnInitializedVarsInNewRunspacesCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidUnInitializedVarsInNewRunspacesDescription);
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
            var astsToProcess = new List<Ast>();

            var inlineScriptCmdletNamesAndAliases = Helper.Instance.CmdletNameAndAliases("InlineScript");
            var jobCmdletNamesAndAliases = Helper.Instance.CmdletNameAndAliases("Start-Job");
            jobCmdletNamesAndAliases.AddRange(Helper.Instance.CmdletNameAndAliases("Start-ThreadJob"));
            var foreachObjectCmdletNamesAndAliases = Helper.Instance.CmdletNameAndAliases("Foreach-Object");
            var invokeCommandCmdletNamesAndAliases = Helper.Instance.CmdletNameAndAliases("Invoke-Command");

            // - `Workflow bar {$foo = 'foo'; InlineScript {$using:foo} }`  On Windows PowerShell only. (see get-help about_InlineScript)
            astsToProcess.AddRange(
                scriptBlockAst.FindAll(
                    predicate: a => a is ScriptBlockExpressionAst scriptBlockExpressionAst &&
                                    scriptBlockExpressionAst.Parent is CommandAst commandAst &&
                                    inlineScriptCmdletNamesAndAliases.Contains(
                                        commandAst.GetCommandName(), StringComparer.OrdinalIgnoreCase),
                    searchNestedScriptBlocks: false));

            // - `Start - Job / Start - ThreadJob - scriptBlock {$using:foo <#'using' required#>} -InitializationScript {$bar <# no 'using' allowed #>}`
            //   Here the catch is, we need to be sure we check the right ScriptBlock. The rule does not apply to InitializationScript.
            //   Shortest unambiguous for for the parameter -InitializationScript is 'ini'. 
            astsToProcess.AddRange(
                scriptBlockAst.FindAll(
                    predicate: a => a is ScriptBlockExpressionAst scriptBlockExpressionAst &&
                                    scriptBlockExpressionAst.Parent is CommandAst commandAst &&
                                    jobCmdletNamesAndAliases.Contains(
                                        commandAst.GetCommandName(), StringComparer.OrdinalIgnoreCase) &&
                                    // we need to exclude the ScriptBlockExpression if it has a CommandParameterAst before it in the CommandElements collection which name starts with 'ini'.
                                    !(commandAst.CommandElements[commandAst.CommandElements.IndexOf(scriptBlockExpressionAst) - 1] is CommandParameterAst parameterAst &&
                                        parameterAst.ParameterName.StartsWith("ini", StringComparison.OrdinalIgnoreCase)),
                    searchNestedScriptBlocks: false));

            // Find all ScriptBlockExpressionAst objects for `Foreach-Object -Parallel`. As for parameter name matching, there are three
            // parameters starting with a 'p': Parallel, PipelineVariable and Process, so we use startsWith 'pa' as the shortest unambiguous form.
            // Because we are already going trough all ScriptBlockAst objects, we do not need to look for nested script blocks here.
            astsToProcess.AddRange(
                scriptBlockAst.FindAll(
                    predicate: a => a is ScriptBlockExpressionAst scriptBlockExpressionAst &&
                                    scriptBlockExpressionAst.Parent is CommandAst commandAst &&
                                    foreachObjectCmdletNamesAndAliases.Contains(
                                        commandAst.GetCommandName(), StringComparer.OrdinalIgnoreCase) &&
                                        commandAst.CommandElements.Any(
                                            e => e is CommandParameterAst parameterAst &&
                                                 parameterAst.ParameterName.StartsWith("pa", StringComparison.OrdinalIgnoreCase)),
                    searchNestedScriptBlocks: false));

            //- `Invoke-Command -ComputerName "baz" -ScriptBlock {$using:foo}`
            //The catch here, is that the `-ComputerName` parameter _has_ to be used, otherwise `$using` is prohibited. 
            //Also tricky; one can open a persistent session, and use subsequent `invoke-command` calls to that, where variables
            //from previous calls are still valid: https://docs.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_remote_variables?view=powershell-7#using-remote-variables
            
            // Because -ComputerName and -Session parameters are in different parameter sets, we can treat them separately
            // Shortest unambiguous for for the parameter -ComputerName is 'com'. 
            astsToProcess.AddRange(scriptBlockAst.FindAll(
                    predicate: a => a is ScriptBlockExpressionAst scriptBlockExpressionAst &&
                                    scriptBlockExpressionAst.Parent is CommandAst commandAst &&
                                    invokeCommandCmdletNamesAndAliases.Contains(
                                        commandAst.GetCommandName(), StringComparer.OrdinalIgnoreCase) &&
                                    commandAst.CommandElements.Any(
                                        e => e is CommandParameterAst parameterAst &&
                                             parameterAst.ParameterName.StartsWith("com", StringComparison.OrdinalIgnoreCase)),
                    searchNestedScriptBlocks: false));

            // Process invoke-command ScriptBlocks that belong to a session
            var scriptBlockExpressionAstsToAnalyze = scriptBlockAst.FindAll(
                predicate: a => a is ScriptBlockExpressionAst scriptBlockExpressionAst &&
                                scriptBlockExpressionAst.Parent is CommandAst commandAst &&
                                invokeCommandCmdletNamesAndAliases.Contains(
                                    commandAst.GetCommandName(), StringComparer.OrdinalIgnoreCase) &&
                                commandAst.CommandElements.Any(
                                    e => e is CommandParameterAst parameterAst &&
                                         parameterAst.ParameterName.StartsWith("session", StringComparison.OrdinalIgnoreCase)), //TODO: find better way to discern between Session, SessionName and SessionOption parameters
                searchNestedScriptBlocks: false);

            // Match ScriptBlocks together that belong to the same session
            var sessionDictionary = new Dictionary<string, List<Ast>>();
            foreach (var ast in scriptBlockExpressionAstsToAnalyze)
            {
                if (!(ast.Parent is CommandAst commandAst))
                    continue;

                var sessionParameterAst = commandAst.CommandElements.First<Ast>(
                    e => e is CommandParameterAst parameterAst &&
                         parameterAst.ParameterName.StartsWith("session", StringComparison.OrdinalIgnoreCase)) as CommandParameterAst;

                if (sessionParameterAst == null)
                    continue;

                var sessionName = commandAst.CommandElements[commandAst.CommandElements.IndexOf(sessionParameterAst) + 1].Extent.Text.Trim();

                if (sessionDictionary.ContainsKey(sessionName))
                {
                    sessionDictionary[sessionName].Add(ast);
                }
                else
                {
                    sessionDictionary.Add(sessionName, new List<Ast>());
                    sessionDictionary[sessionName].Add(ast);
                }
            }

            var nonAssignedNonUsingVars = new List<VariableExpressionAst>();

            foreach (var session in sessionDictionary)
            {
                // Find all variables that are assigned within these ScriptBlocks that are part of one session
                List<VariableExpressionAst> varsInAssignments = new List<VariableExpressionAst>();
                foreach (var ast in session.Value)
                {
                    varsInAssignments.AddRange(FindVarsInAssignmentAsts(ast));
                }

                // Find all variables that are not locally assigned, and don't have $using: scope modifier
                foreach (var ast in session.Value)
                {
                    nonAssignedNonUsingVars.AddRange(FindNonAssignedNonUsingVarAsts(ast, varsInAssignments));
                }
            }


            // Process rest of the Asts
            foreach (var ast in astsToProcess)
            {
                if (ast == null)
                    continue;

                var varsInAssignments = FindVarsInAssignmentAsts(ast);
                
                nonAssignedNonUsingVars.AddRange(FindNonAssignedNonUsingVarAsts(ast, varsInAssignments));
            }

            foreach (var variableExpression in nonAssignedNonUsingVars)
            {
                if (variableExpression == null)
                    continue;

                yield return new DiagnosticRecord(
                    message: string.Format(CultureInfo.CurrentCulture,
                        Strings.AvoidUnInitializedVarsInNewRunspacesError, variableExpression.ToString()),
                    extent: variableExpression.Extent,
                    ruleName: GetName(),
                    severity: DiagnosticSeverity.Warning,
                    scriptPath: fileName,
                    ruleId: variableExpression.ToString());
            }
        }

        private static IEnumerable<VariableExpressionAst> FindVarsInAssignmentAsts (Ast ast)
        {
            // Find all variables that are assigned within this ast
            return ast.FindAll(
                predicate: a => a is VariableExpressionAst varExpr &&
                                varExpr.Parent is AssignmentStatementAst assignment &&
                                assignment.Left.Equals(varExpr),
                searchNestedScriptBlocks: true).Select(a => a as VariableExpressionAst);
        }

        private static IEnumerable<VariableExpressionAst> FindNonAssignedNonUsingVarAsts(Ast ast, IEnumerable<VariableExpressionAst> varsInAssignments)
        {
            // Find all variables that are not locally assigned, and don't have $using: scope modifier
            return ast.FindAll(
                predicate: a => a is VariableExpressionAst varAst &&
                                !(varAst.Parent is UsingExpressionAst) &&
                                varsInAssignments.All(b => b.VariablePath.UserPath != varAst.VariablePath.UserPath) &&
                                !Helper.Instance.HasSpecialVars(varAst.VariablePath.UserPath),
                searchNestedScriptBlocks: true).Select(a => a as VariableExpressionAst);
        }
    }
}

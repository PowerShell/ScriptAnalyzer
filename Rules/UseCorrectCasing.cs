﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Management.Automation.Language;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;
using System.Linq;
using System.Management.Automation;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// UseCorrectCasing: Check if cmdlet is cased correctly.
    /// </summary>
#if !CORECLR
    [Export(typeof(IScriptRule))]
#endif
    public class UseCorrectCasing : ConfigurableRule //IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Analyze the script to check if cmdlet alias is used.
        /// </summary>
        public override IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            // Finds all CommandAsts.
            IEnumerable<Ast> foundAsts = ast.FindAll(testAst => testAst is CommandAst, true);

            // Iterates all CommandAsts and check the command name.
            foreach (CommandAst commandAst in foundAsts)
            {
                // Check if the command ast should be ignored
                if (IgnoreCommandast(commandAst))
                {
                    continue;
                }

                string commandName = commandAst.GetCommandName();

                // Handles the exception caused by commands like, {& $PLINK $args 2> $TempErrorFile}.
                // You can also review the remark section in following document,
                // MSDN: CommandAst.GetCommandName Method
                if (commandName == null)
                {
                    continue;
                }

                using (var powershell = System.Management.Automation.PowerShell.Create())
                {
                    var psCommand = powershell.AddCommand("Get-Command")
                        .AddParameter("Name", commandName)
                        .AddParameter("ErrorAction", "SilentlyContinue");

                    //if (commandName != null)
                    //{
                    //    psCommand.AddParameter("CommandType", commandType);
                    //}

                    var commandInfo = psCommand.Invoke<CommandInfo>()
                             .FirstOrDefault();
                    var name = commandInfo.Name;
                    var modulename = commandInfo.ModuleName;
                    var fullyqual = $"{modulename}\\{name}";

                    if (!name.Equals(commandName, StringComparison.Ordinal))
                    {
                        yield return new DiagnosticRecord(
                            string.Format(CultureInfo.CurrentCulture, Strings.AvoidUsingCmdletAliasesError, commandName, name),
                            GetCommandExtent(commandAst),
                            GetName(),
                            DiagnosticSeverity.Warning,
                            fileName,
                            commandName,
                            suggestedCorrections: GetCorrectionExtent(commandAst, name));
                    }
                }

            }
        }

        /// <summary>
        /// Checks commandast of the form "[commandElement0] = [CommandElement2]". This typically occurs in a DSC configuration.
        /// </summary>
        private bool IgnoreCommandast(CommandAst cmdAst)
        {
            if (cmdAst.CommandElements.Count == 3)
            {
                var element = cmdAst.CommandElements[1] as StringConstantExpressionAst;
                if (element != null && element.Value.Equals("="))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// For a command like "gci -path c:", returns the extent of "gci" in the command
        /// </summary>
        private IScriptExtent GetCommandExtent(CommandAst commandAst)
        {
            var cmdName = commandAst.GetCommandName();
            foreach (var cmdElement in commandAst.CommandElements)
            {
                var stringConstExpressinAst = cmdElement as StringConstantExpressionAst;
                if (stringConstExpressinAst != null)
                {
                    if (stringConstExpressinAst.Value.Equals(cmdName))
                    {
                        return stringConstExpressinAst.Extent;
                    }
                }
            }
            return commandAst.Extent;
        }

        /// <summary>
        /// Creates a list containing suggested correction
        /// </summary>
        /// <param name="cmdAst">Command AST of an alias</param>
        /// <param name="cmdletName">Full name of the alias</param>
        /// <returns>Retruns a list of suggested corrections</returns>
        private List<CorrectionExtent> GetCorrectionExtent(CommandAst cmdAst, string cmdletName)
        {
            var corrections = new List<CorrectionExtent>();
            var alias = cmdAst.GetCommandName();
            var description = string.Format(
                CultureInfo.CurrentCulture,
                Strings.AvoidUsingCmdletAliasesCorrectionDescription,
                alias,
                cmdletName);
            var cmdExtent = GetCommandExtent(cmdAst);
            corrections.Add(new CorrectionExtent(
                cmdExtent.StartLineNumber,
                cmdExtent.EndLineNumber,
                cmdExtent.StartColumnNumber,
                cmdExtent.EndColumnNumber,
                cmdletName,
                cmdAst.Extent.File,
                description));
            return corrections;
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public override string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), "UseCorrectCasing");//Strings.AvoidUsingCmdletAliasesName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public override string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, "UseCorrectCasing");// Strings.AvoidUsingCmdletAliasesCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public override string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidUsingCmdletAliasesDescription);
        }

        /// <summary>
        /// GetSourceType: Retrieves the type of the rule, Builtin, Managed or Module.
        /// </summary>
        public override SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }

        /// <summary>
        /// GetSeverity: Retrieves the severity of the rule: error, warning of information.
        /// </summary>
        /// <returns></returns>
        public override RuleSeverity GetSeverity()
        {
            return RuleSeverity.Warning;
        }

        /// <summary>
        /// GetSourceName: Retrieves the name of the module/assembly the rule is from.
        /// </summary>
        public override string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }
    }
}
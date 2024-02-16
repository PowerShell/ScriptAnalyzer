// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Management.Automation.Language;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
using System.Management.Automation;
using System.Linq;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// UseCorrectCasing: Check if cmdlet is cased correctly.
    /// </summary>
#if !CORECLR
    [Export(typeof(IScriptRule))]
#endif
    public class UseCorrectCasing : ConfigurableRule
    {

        /// <summary>If true, require the case of all operators to be lowercase.</summary>
        [ConfigurableRuleProperty(defaultValue: true)]
        public bool CheckOperator { get; set; }

        /// <summary>If true, require the case of all keywords to be lowercase.</summary>
        [ConfigurableRuleProperty(defaultValue: true)]
        public bool CheckKeyword { get; set; }

        /// <summary>If true, require the case of all commands to match their actual casing.</summary>
        [ConfigurableRuleProperty(defaultValue: true)]
        public bool CheckCommands { get; set; }

        private TokenFlags operators = TokenFlags.BinaryOperator | TokenFlags.UnaryOperator;

        /// <summary>
        /// AnalyzeScript: Analyze the script to check if cmdlet alias is used.
        /// </summary>
        public override IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast is null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            if (CheckOperator || CheckKeyword)
            {
                // Iterate tokens to look for the keywords and operators
                for (int i = 0; i < Helper.Instance.Tokens.Length; i++)
                {
                    Token token = Helper.Instance.Tokens[i];

                    if (CheckKeyword && ((token.TokenFlags & TokenFlags.Keyword) != 0))
                    {
                        string correctCase = token.Text.ToLowerInvariant();
                        if (!token.Text.Equals(correctCase, StringComparison.Ordinal))
                        {
                            yield return GetDiagnosticRecord(token, fileName, correctCase, Strings.UseCorrectCasingKeywordError);
                        }
                        continue;
                    }

                    if (CheckOperator && ((token.TokenFlags & operators) != 0))
                    {
                        string correctCase = token.Text.ToLowerInvariant();
                        if (!token.Text.Equals(correctCase, StringComparison.Ordinal))
                        {
                            yield return GetDiagnosticRecord(token, fileName, correctCase, Strings.UseCorrectCasingOperatorError);
                        }
                    }
                }
            }

            if (CheckCommands)
            {
                // Iterate command ASTs for command and parameter names
                IEnumerable<Ast> commandAsts = ast.FindAll(testAst => testAst is CommandAst, true);

                // Iterates all CommandAsts and check the command name.
                foreach (CommandAst commandAst in commandAsts)
                {
                    string commandName = commandAst.GetCommandName();

                    // Handles the exception caused by commands like, {& $PLINK $args 2> $TempErrorFile}.
                    // You can also review the remark section in following document,
                    // MSDN: CommandAst.GetCommandName Method
                    if (commandName == null)
                    {
                        continue;
                    }

                    var commandInfo = Helper.Instance.GetCommandInfo(commandName);
                    if (commandInfo == null || commandInfo.CommandType == CommandTypes.ExternalScript || commandInfo.CommandType == CommandTypes.Application)
                    {
                        continue;
                    }

                    var shortName = commandInfo.Name;
                    var fullyqualifiedName = $"{commandInfo.ModuleName}\\{shortName}";
                    var isFullyQualified = commandName.Equals(fullyqualifiedName, StringComparison.OrdinalIgnoreCase);
                    var correctlyCasedCommandName = isFullyQualified ? fullyqualifiedName : shortName;

                    if (!commandName.Equals(correctlyCasedCommandName, StringComparison.Ordinal))
                    {
                        yield return GetDiagnosticRecord(commandAst, fileName, correctlyCasedCommandName, Strings.UseCorrectCasingError);
                    }

                    var commandParameterAsts = commandAst.FindAll(
                        testAst => testAst is CommandParameterAst, true).Cast<CommandParameterAst>();
                    Dictionary<string, ParameterMetadata> availableParameters;
                    try
                    {
                        availableParameters = commandInfo.Parameters;
                    }
                    // It's a known issue that objects from PowerShell can have a runspace affinity,
                    // therefore if that happens, we query a fresh object instead of using the cache.
                    // https://github.com/PowerShell/PowerShell/issues/4003
                    catch (InvalidOperationException)
                    {
                        commandInfo = Helper.Instance.GetCommandInfo(commandName, bypassCache: true);
                        availableParameters = commandInfo.Parameters;
                    }
                    foreach (var commandParameterAst in commandParameterAsts)
                    {
                        var parameterName = commandParameterAst.ParameterName;
                        if (availableParameters.TryGetValue(parameterName, out ParameterMetadata parameterMetaData))
                        {
                            var correctlyCasedParameterName = parameterMetaData.Name;
                            if (!parameterName.Equals(correctlyCasedParameterName, StringComparison.Ordinal))
                            {
                                yield return GetDiagnosticRecord(commandParameterAst, fileName, correctlyCasedParameterName, Strings.UseCorrectCasingError);
                            }
                        }
                    }
                }
            }
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

        private IEnumerable<CorrectionExtent> GetCorrectionExtent(Ast ast, IScriptExtent extent, string correctlyCaseName)
        {
            var correction = new CorrectionExtent(
                extent.StartLineNumber,
                extent.EndLineNumber,
                // For parameters, add +1 because of the dash before the parameter name
                (ast is CommandParameterAst ? extent.StartColumnNumber + 1 : extent.StartColumnNumber),
                // and do not use EndColumnNumber property, because sometimes it's all of: -ParameterName:$ParameterValue
                (ast is CommandParameterAst ? extent.StartColumnNumber + 1 + ((CommandParameterAst)ast).ParameterName.Length : extent.EndColumnNumber),
                correctlyCaseName,
                extent.File,
                GetDescription());
            yield return correction;
        }

        private DiagnosticRecord GetDiagnosticRecord(Token token, string fileName, string correction, string message)
        {
            var extents = new[]
            {
                new CorrectionExtent(
                    token.Extent.StartLineNumber,
                    token.Extent.EndLineNumber,
                    token.Extent.StartColumnNumber,
                    token.Extent.EndColumnNumber,
                    correction,
                    token.Extent.File,
                    GetDescription())
            };

            return new DiagnosticRecord(
                string.Format(CultureInfo.CurrentCulture, message, token.Text, correction),
                token.Extent,
                GetName(),
                DiagnosticSeverity.Information,
                fileName,
                correction, // return the keyword case as the id, so you can turn this off for specific keywords...
                suggestedCorrections: extents);
        }

        private DiagnosticRecord GetDiagnosticRecord(Ast ast, string fileName, string correction, string message)
        {
            var extent = ast is CommandAst ? GetCommandExtent((CommandAst)ast) : ast.Extent;
            return new DiagnosticRecord(
                string.Format(CultureInfo.CurrentCulture, message, extent.Text, correction),
                extent,
                GetName(),
                DiagnosticSeverity.Warning,
                fileName,
                correction,
                suggestedCorrections: GetCorrectionExtent(ast, extent, correction));
        }

        private DiagnosticRecord GetDiagnosticRecord(CommandParameterAst ast, string fileName, string correction, string message)
        {
            var extent = ast.Extent;
            return new DiagnosticRecord(
                string.Format(CultureInfo.CurrentCulture, message, extent.Text, correction),
                extent,
                GetName(),
                DiagnosticSeverity.Warning,
                fileName,
                correction,
                suggestedCorrections: GetCorrectionExtent(ast, extent, correction));
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public override string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.UseCorrectCasingName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public override string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseCorrectCasingCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public override string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseCorrectCasingDescription);
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
            return RuleSeverity.Information;
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

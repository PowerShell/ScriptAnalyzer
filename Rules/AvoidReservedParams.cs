// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;
using System.Reflection;
using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
#if !CORECLR
using System.ComponentModel.Composition;
#endif
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Internal;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer.BuiltinRules
{
    /// <summary>
    /// AvoidReservedParams: Analyzes the ast to check for reserved parameters in function definitions.
    /// </summary>
#if !CORECLR
[Export(typeof(IScriptRule))]
#endif
    public class AvoidReservedParams : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Analyzes the ast to check for reserved parameters in function definitions.
        /// </summary>
        /// <param name="ast">The script's ast</param>
        /// <param name="fileName">The script's file name</param>
        /// <returns>A List of diagnostic results of this rule</returns>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName) {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            IEnumerable<Ast> funcAsts = ast.FindAll(item => item is FunctionDefinitionAst, true);

#if CORECLR
            List<string> commonParamNames = TypeExtensions.GetProperties(typeof(CommonParameters)).Select(param => param.Name).ToList();
#else
            List<string> commonParamNames = typeof(CommonParameters).GetProperties().Select(param => param.Name).ToList();
#endif

            foreach (FunctionDefinitionAst funcAst in funcAsts)
            {
                // this rule only applies to function with param block
                if (funcAst.Body != null && funcAst.Body.ParamBlock != null
                    && funcAst.Body.ParamBlock.Attributes != null && funcAst.Body.ParamBlock.Parameters != null)
                {
                    // no cmdlet binding
                    if (!funcAst.Body.ParamBlock.Attributes.Any(attr => attr.TypeName.GetReflectionType() == typeof(CmdletBindingAttribute)))
                    {
                        continue;
                    }

                    foreach (ParameterAst paramAst in funcAst.Body.ParamBlock.Parameters)
                    {
                        string paramName = paramAst.Name.VariablePath.UserPath;

                        if (commonParamNames.Contains(paramName, StringComparer.OrdinalIgnoreCase))
                        {
                            yield return new DiagnosticRecord(string.Format(CultureInfo.CurrentCulture, Strings.ReservedParamsError, funcAst.Name, paramName),
                                paramAst.Extent, GetName(), GetDiagnosticSeverity(), fileName);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// GetName: Retrieves the name of this rule.
        /// </summary>
        /// <returns>The name of this rule</returns>
        public string GetName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.ReservedParamsName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.ReservedParamsCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription() {
            return string.Format(CultureInfo.CurrentCulture, Strings.ReservedParamsDescription);
        }

        /// <summary>
        /// Method: Retrieves the type of the rule: builtin, managed or module.
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
            return RuleSeverity.Error;
        }

        /// <summary>
        /// Gets the severity of the returned diagnostic record: error, warning, or information.
        /// </summary>
        /// <returns></returns>
        public DiagnosticSeverity GetDiagnosticSeverity()
        {
            return DiagnosticSeverity.Error;
        }

        /// <summary>
        /// Method: Retrieves the module/assembly name the rule is from.
        /// </summary>
        public string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }
    }
}





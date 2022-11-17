// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
    /// AvoidGeneralCatch: Check if catch clause type is RuntimeException and caution against using it  
    /// </summary>
#if !CORECLR
[Export(typeof(IScriptRule))]
#endif
    public class AvoidGeneralCatch : IScriptRule
    {
        /// <summary>
        /// AnalyzeScript: Analyze the script to check if any empty catch block is used.
        /// </summary>
        public IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null) throw new ArgumentNullException(Strings.NullAstErrorMessage);

            // Finds all CommandAsts.
            IEnumerable<Ast> foundAsts = ast.FindAll(testAst => testAst is CatchClauseAst, true);

            // Iterates all CatchClauseAst and check the statements count.
            foreach (Ast foundAst in foundAsts)
            {
                CatchClauseAst catchAst = (CatchClauseAst)foundAst;

                if (catchAst.CatchTypes.Count == 0) 
                {
                    yield return new DiagnosticRecord(string.Format(CultureInfo.CurrentCulture, Strings.AvoidGeneralCatchError),
                        catchAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName);
                }
                else
                {

                    foreach (TypeConstraintAst caughtAst in catchAst.CatchTypes) {
                        if (string.Equals(caughtAst.TypeName.FullName, "RuntimeException", StringComparison.CurrentCultureIgnoreCase) | string.Equals(caughtAst.TypeName.FullName, "System.Management.Automation.RuntimeException", StringComparison.CurrentCultureIgnoreCase)); {
                        
                        yield return new DiagnosticRecord(string.Format(CultureInfo.CurrentCulture, Strings.AvoidGeneralCatchError),
                            caughtAst.Extent, GetName(), DiagnosticSeverity.Warning, fileName);

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
            return string.Format(CultureInfo.CurrentCulture, Strings.NameSpaceFormat, GetSourceName(), Strings.AvoidGeneralCatchName);
        }

        /// <summary>
        /// GetCommonName: Retrieves the common name of this rule.
        /// </summary>
        /// <returns>The common name of this rule</returns>
        public string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidGeneralCatchCommonName);
        }

        /// <summary>
        /// GetDescription: Retrieves the description of this rule.
        /// </summary>
        /// <returns>The description of this rule</returns>
        public string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.AvoidGeneralCatchDescription);
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
            return RuleSeverity.Warning;
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

    





// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer
{
    /// <summary>
    /// A class to provide code formatting capability.
    /// </summary>
    public class Formatter
    {
        /// <summary>
        /// Format a powershell script.
        /// </summary>
        /// <param name="scriptDefinition">A string representing a powershell script.</param>
        /// <param name="settings">Settings to be used for formatting</param>
        /// <param name="range">The range in which formatting should take place.</param>
        /// <param name="cmdlet">The cmdlet object that calls this method.</param>
        /// <returns></returns>
        public static string Format<TCmdlet>(
            string scriptDefinition,
            Settings settings,
            Range range,
            TCmdlet cmdlet) where TCmdlet : PSCmdlet, IOutputWriter
        {
            // todo implement notnull attribute for such a check
            ValidateNotNull(scriptDefinition, "scriptDefinition");
            ValidateNotNull(settings, "settings");
            ValidateNotNull(cmdlet, "cmdlet");

            Helper.Instance = new Helper(cmdlet.SessionState.InvokeCommand);
            Helper.Instance.Initialize();

            var ruleOrder = new string[]
            {
                "PSPlaceCloseBrace",
                "PSPlaceOpenBrace",
                "PSUseConsistentWhitespace",
                "PSUseConsistentIndentation",
                "PSAlignAssignmentStatement",
                "PSUseCorrectCasing",
                "PSAvoidUsingCmdletAliases",
                "PSAvoidUsingDoubleQuotesForConstantString",
                "PSAvoidSemicolonsAsLineTerminators",
                "PSAvoidExclaimOperator",
                "PSAvoidTrailingWhitespace",
            };

            var text = new EditableText(scriptDefinition);
            ScriptBlockAst scriptAst = null;
            Token[] scriptTokens = null;
            bool skipParsing = false;
            foreach (var rule in ruleOrder)
            {
                if (!settings.RuleArguments.ContainsKey(rule))
                {
                    continue;
                }

                var currentSettings = GetCurrentSettings(settings, rule);
                ScriptAnalyzer.Instance.UpdateSettings(currentSettings);
                ScriptAnalyzer.Instance.Initialize(cmdlet, null, null, null, null, true, SuppressionPreference.Omit);

                text = ScriptAnalyzer.Instance.Fix(text, range, skipParsing, out Range updatedRange, out bool fixesWereApplied, ref scriptAst, ref scriptTokens, skipVariableAnalysis: true);
                skipParsing = !fixesWereApplied;
                range = updatedRange;
            }

            return text.ToString();
        }

        private static void ValidateNotNull<T>(T obj, string name)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(name);
            }
        }

        private static Settings GetCurrentSettings(Settings settings, string rule)
        {
            return new Settings(new Hashtable()
            {
                {"IncludeRules", new string[] {rule}},
                {"Rules", new Hashtable() { { rule, new Hashtable(settings.RuleArguments[rule]) } } }
            });
        }
    }
}

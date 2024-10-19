// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer
{

    /// <summary>
    /// This Helper class contains utility/helper functions for classes in ScriptAnalyzer.
    /// </summary>
    public class Helper
    {
        #region Private members

        private CommandInvocationIntrinsics invokeCommand;
        private readonly static Version minSupportedPSVersion = new Version(3, 0);
        private Dictionary<string, Dictionary<string, object>> ruleArguments;
        private PSVersionTable psVersionTable;

        private readonly Lazy<CommandInfoCache> _commandInfoCacheLazy;
        private readonly object _testModuleManifestLock = new object();

        #endregion

        #region Singleton
        private static object syncRoot = new Object();

        private static Helper instance;

        /// <summary>
        /// The helper instance that handles utility functions
        /// </summary>
        public static Helper Instance
        {
            get
            {
                if (instance == null)
                {
                    Instance = new Helper();
                }

                return instance;
            }
            internal set
            {
                lock (syncRoot)
                {
                    if (instance == null)
                    {
                        instance = value;
                    }
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Dictionary contains mapping of cmdlet to alias
        /// </summary>
        private Dictionary<String, List<String>> CmdletToAliasDictionary;

        /// <summary>
        /// Dictionary contains mapping of alias to cmdlet
        /// </summary>
        private Dictionary<String, String> AliasToCmdletDictionary;

        internal TupleComparer tupleComparer = new TupleComparer();

        /// <summary>
        /// My Tokens
        /// </summary>
        public Token[] Tokens { get; set; }

        /// <summary>
        /// Key of the dictionary is keyword or command like configuration or workflows.
        /// Value is a list of integer (in pairs). The first item in a pair is
        /// the starting position of the open curly brace and the second item
        /// is the closing position of the closing curly brace.
        /// </summary>
        private Dictionary<String, List<Tuple<int, int>>> KeywordBlockDictionary;

        /// <summary>
        /// Key of dictionary is ast, value is the corresponding variableanalysis
        /// </summary>
        private Dictionary<Ast, VariableAnalysis> VariableAnalysisDictionary;

        private string[] functionScopes = new string[] { "global:", "local:", "script:", "private:"};

        private string[] variableScopes = new string[] { "global:", "local:", "script:", "private:", "variable:", ":"};

        /// <summary>
        /// Store of command info objects for commands. Memoizes results.
        /// </summary>
        private CommandInfoCache CommandInfoCache => _commandInfoCacheLazy.Value;

        #endregion

        /// <summary>
        /// Initializes the Helper class.
        /// </summary>
        private Helper()
        {
            _commandInfoCacheLazy = new Lazy<CommandInfoCache>(() => new CommandInfoCache());
        }

        /// <summary>
        /// Initializes the Helper class.
        /// </summary>
        /// <param name="invokeCommand">
        /// A CommandInvocationIntrinsics instance for use in gathering
        /// information about available commands and aliases.
        /// </param>
        public Helper(
            CommandInvocationIntrinsics invokeCommand
            ): this()
        {
            this.invokeCommand = invokeCommand;
        }

        #region Methods
        /// <summary>
        /// Initialize : Initializes dictionary of alias.
        /// </summary>
        public void Initialize()
        {
            CmdletToAliasDictionary = new Dictionary<String, List<String>>(StringComparer.OrdinalIgnoreCase);
            AliasToCmdletDictionary = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            KeywordBlockDictionary = new Dictionary<String, List<Tuple<int, int>>>(StringComparer.OrdinalIgnoreCase);
            VariableAnalysisDictionary = new Dictionary<Ast, VariableAnalysis>();
            ruleArguments = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<CommandInfo> aliases = this.invokeCommand.GetCommands("*", CommandTypes.Alias, true);

            foreach (AliasInfo aliasInfo in aliases)
            {
                if (!CmdletToAliasDictionary.ContainsKey(aliasInfo.Definition))
                {
                    CmdletToAliasDictionary.Add(aliasInfo.Definition, new List<String>() { aliasInfo.Name });
                }
                else
                {
                    CmdletToAliasDictionary[aliasInfo.Definition].Add(aliasInfo.Name);
                }

                AliasToCmdletDictionary.Add(aliasInfo.Name, aliasInfo.Definition);
            }
        }

        /// <summary>
        /// Returns all the rule arguments
        /// </summary>
        /// <returns>Dictionary that maps between rule name to their named arguments</returns>
        public Dictionary<string, Dictionary<string, object>> GetRuleArguments()
        {
            return ruleArguments;
        }

        /// <summary>
        /// Get the parameters corresponding to the given rule name
        /// </summary>
        /// <param name="ruleName"></param>
        /// <returns>Dictionary of argument names mapped to values. If ruleName is not a valid key, returns null</returns>
        public Dictionary<string, object> GetRuleArguments(string ruleName)
        {
            if (ruleArguments.ContainsKey(ruleName))
            {
                return ruleArguments[ruleName];
            }
            return null;
        }

        /// <summary>
        /// Sets the arguments for consumption by rules
        /// </summary>
        /// <param name="ruleArgs">A hashtable with rule names as keys</param>
        public void SetRuleArguments(Dictionary<string, object> ruleArgs)
        {
            if (ruleArgs == null)
            {
                return;
            }

            if (ruleArgs.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                throw new ArgumentException(
                    "Input dictionary should have OrdinalIgnoreCase comparer.",
                    "ruleArgs");
            }
            var ruleArgsDict = new Dictionary<string, Dictionary<string, object>>();
            foreach (var rule in ruleArgs.Keys)
            {
                var argsDict = ruleArgs[rule] as Dictionary<string, object>;
                if (argsDict == null)
                {
                    return;
                }
                ruleArgsDict[rule] = argsDict;
            }
            ruleArguments = ruleArgsDict;
        }

        /// <summary>
        /// Given a cmdlet, return the list of all the aliases.
        /// Also include the original name in the list.
        /// </summary>
        /// <param name="Cmdlet">Name of the cmdlet</param>
        /// <returns></returns>
        public List<String> CmdletNameAndAliases(String Cmdlet)
        {
            List<String> results = new List<String>();
            results.Add(Cmdlet);

            if (CmdletToAliasDictionary.ContainsKey(Cmdlet))
            {
                results.AddRange(CmdletToAliasDictionary[Cmdlet]);
            }

            return results;
        }

        /// <summary>
        /// Given an alias, returns the cmdlet.
        /// </summary>
        /// <param name="Alias"></param>
        /// <returns></returns>
        public string GetCmdletNameFromAlias(String Alias)
        {
            if (AliasToCmdletDictionary.ContainsKey(Alias))
            {
                return AliasToCmdletDictionary[Alias];
            }

            return String.Empty;
        }

        /// <summary>
        /// Given a file path, checks whether the file is part of a dsc resource module
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public bool IsDscResourceModule(string filePath)
        {
            DirectoryInfo dscResourceParent = Directory.GetParent(filePath);
            if (null != dscResourceParent)
            {
                DirectoryInfo dscResourcesFolder = Directory.GetParent(dscResourceParent.ToString());
                if (null != dscResourcesFolder)
                {
                    if (String.Equals(dscResourcesFolder.Name, "dscresources", StringComparison.OrdinalIgnoreCase))
                    {
                        // Step 2: Ensure there is a Schema.mof in the same folder as the artifact
                        string schemaMofParentFolder = Directory.GetParent(filePath).ToString();
                        string[] schemaMofFile = Directory.GetFiles(schemaMofParentFolder, "*.schema.mof");

                        // Ensure Schema file exists and is the only one in the DSCResource folder
                        if (schemaMofFile != null && schemaMofFile.Count() == 1)
                        {
                            // Run DSC Rules only on module that matches the schema.mof file name without extension
                            if (String.Equals(schemaMofFile[0].Replace("schema.mof", "psm1"), filePath, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the module manifest
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="errorRecord"></param>
        /// <returns>Returns a object of type PSModuleInfo</returns>
        public PSModuleInfo GetModuleManifest(string filePath, out IEnumerable<ErrorRecord> errorRecord)
        {
            errorRecord = null;
            PSModuleInfo psModuleInfo = null;
            Collection<PSObject> psObj = null;
            // Test-ModuleManifest is not thread safe
            lock (_testModuleManifestLock)
            {
                using (var ps = System.Management.Automation.PowerShell.Create())
                {
                    ps.AddCommand("Test-ModuleManifest")
                      .AddParameter("Path", filePath)
                      .AddParameter("WarningAction", ActionPreference.SilentlyContinue);
                    try
                    {
                        psObj = ps.Invoke();
                    }
                    catch (CmdletInvocationException e)
                    {
                        // Invoking Test-ModuleManifest on a module manifest that doesn't have all the valid keys
                        // throws a NullReferenceException. This is probably a bug in Test-ModuleManifest and hence
                        // we consume it to allow execution of the of this method.
                        if (e.InnerException == null || e.InnerException.GetType() != typeof(System.NullReferenceException))
                        {
                            throw;
                        }
                    }
                    if (ps.HadErrors && ps.Streams != null && ps.Streams.Error != null)
                    {
                        var errorRecordArr = new ErrorRecord[ps.Streams.Error.Count];
                        ps.Streams.Error.CopyTo(errorRecordArr, 0);
                        errorRecord = errorRecordArr;
                    }
                    if (psObj != null && psObj.Any() && psObj[0] != null)
                    {
                        psModuleInfo = psObj[0].ImmediateBaseObject as PSModuleInfo;
                    }
                }
            }
            return psModuleInfo;
        }

        /// <summary>
        /// Checks if the error record is MissingMemberException
        /// </summary>
        /// <param name="errorRecord"></param>
        /// <returns>Returns a boolean value indicating the presence of MissingMemberException</returns>
        public static bool IsMissingManifestMemberException(ErrorRecord errorRecord)
        {
            return errorRecord.CategoryInfo != null
                && errorRecord.CategoryInfo.Category == ErrorCategory.ResourceUnavailable
                && string.Equals("MissingMemberException", errorRecord.CategoryInfo.Reason, StringComparison.OrdinalIgnoreCase);
        }

        public IEnumerable<string> GetStringsFromExpressionAst(ExpressionAst exprAst)
        {
            if (exprAst == null)
            {
                throw new ArgumentNullException("exprAst");
            }

            var result = new List<string>();
            if (exprAst is StringConstantExpressionAst)
            {
                result.Add((exprAst as StringConstantExpressionAst).Value);
            }
            // Array of the form "v-n", "v-n1"
            else if (exprAst is ArrayLiteralAst)
            {
                result.AddRange(Helper.Instance.GetStringsFromArrayLiteral(exprAst as ArrayLiteralAst));
            }
            // Array of the form @("v-n", "v-n1")
            else if (exprAst is ArrayExpressionAst)
            {
                ArrayExpressionAst arrExAst = exprAst as ArrayExpressionAst;
                if (arrExAst.SubExpression != null && arrExAst.SubExpression.Statements != null)
                {
                    foreach (StatementAst stAst in arrExAst.SubExpression.Statements)
                    {
                        if (stAst is PipelineAst)
                        {
                            PipelineAst pipeAst = stAst as PipelineAst;
                            if (pipeAst.PipelineElements != null)
                            {
                                foreach (CommandBaseAst cmdBaseAst in pipeAst.PipelineElements)
                                {
                                    if (cmdBaseAst is CommandExpressionAst)
                                    {
                                        result.AddRange(Helper.Instance.GetStringsFromArrayLiteral((cmdBaseAst as CommandExpressionAst).Expression as ArrayLiteralAst));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get the list of exported function by analyzing the ast
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public HashSet<string> GetExportedFunction(Ast ast)
        {
            HashSet<string> exportedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> exportFunctionsCmdlet = Helper.Instance.CmdletNameAndAliases("export-modulemember");

            // find functions exported
            IEnumerable<Ast> cmdAsts = ast.FindAll(item => item is CommandAst
                && exportFunctionsCmdlet.Contains((item as CommandAst).GetCommandName(), StringComparer.OrdinalIgnoreCase), true);

            CommandInfo exportMM = Helper.Instance.GetCommandInfo("export-modulemember", CommandTypes.Cmdlet);

            // switch parameters
            IEnumerable<ParameterMetadata> switchParams = (exportMM != null) ? exportMM.Parameters.Values.Where<ParameterMetadata>(pm => pm.SwitchParameter) : Enumerable.Empty<ParameterMetadata>();

            if (exportMM == null)
            {
                return exportedFunctions;
            }

            foreach (CommandAst cmdAst in cmdAsts)
            {
                if (cmdAst.CommandElements == null || cmdAst.CommandElements.Count < 2)
                {
                    continue;
                }

                int i = 1;

                while (i < cmdAst.CommandElements.Count)
                {
                    CommandElementAst ceAst = cmdAst.CommandElements[i];
                    ExpressionAst exprAst = null;

                    if (ceAst is CommandParameterAst)
                    {
                        var paramAst = ceAst as CommandParameterAst;
                        var param = exportMM.ResolveParameter(paramAst.ParameterName);

                        if (param == null)
                        {
                            i += 1;
                            continue;
                        }

                        if (string.Equals(param.Name, "function", StringComparison.OrdinalIgnoreCase))
                        {
                            // checks for the case of -Function:"verb-nouns"
                            if (paramAst.Argument != null)
                            {
                                exprAst = paramAst.Argument;
                            }
                            // checks for the case of -Function "verb-nouns"
                            else if (i < cmdAst.CommandElements.Count - 1)
                            {
                                i += 1;
                                exprAst = cmdAst.CommandElements[i] as ExpressionAst;
                            }
                        }
                        // some other parameter. we just checks whether the one after this is positional
                        else if (i < cmdAst.CommandElements.Count - 1)
                        {
                            // the next element is a parameter like -module so just move to that one
                            if (cmdAst.CommandElements[i + 1] is CommandParameterAst)
                            {
                                i += 1;
                                continue;
                            }

                            // not a switch parameter so the next element is definitely the argument to this parameter
                            if (paramAst.Argument == null && !switchParams.Contains(param))
                            {
                                // skips the next element
                                i += 1;
                            }

                            i += 1;
                            continue;
                        }
                    }
                    else if (ceAst is ExpressionAst)
                    {
                        exprAst = ceAst as ExpressionAst;
                    }

                    if (exprAst != null)
                    {
                        exportedFunctions.UnionWith(Helper.Instance.GetStringsFromExpressionAst(exprAst));
                    }

                    i += 1;
                }
            }

            return exportedFunctions;
        }

        /// <summary>
        /// Given a filePath. Returns true if it is a powershell help file
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public bool IsHelpFile(string filePath)
        {
            return filePath != null && File.Exists(filePath) && Path.GetFileName(filePath).StartsWith("about_", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(filePath).EndsWith(".help.txt", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Given an AST, checks whether dsc resource is class based or not
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public bool IsDscResourceClassBased(ScriptBlockAst ast)
        {
            if (null == ast)
            {
                return false;
            }

            #if !(PSV3||PSV4)

            List<string> dscResourceFunctionNames = new List<string>(new string[] { "Test", "Get", "Set" });

            IEnumerable<Ast> dscClasses = ast.FindAll(item =>
                item is TypeDefinitionAst
                && ((item as TypeDefinitionAst).IsClass)
                && (item as TypeDefinitionAst).Attributes.Any(attr => String.Equals("DSCResource", attr.TypeName.FullName, StringComparison.OrdinalIgnoreCase)), true);

            // Found one or more classes marked with DscResource attribute
            // So this might be a DscResource. Further validation will be performed by the individual rules
            if (null != dscClasses && 0 < dscClasses.Count())
            {
                return true;
            }

            #endif

            return false;
        }

        private string NameWithoutScope(string name, string[] scopes)
        {
            if (String.IsNullOrWhiteSpace(name) || scopes == null)
            {
                return name;
            }

            // checks whether function name starts with scope
            foreach (string scope in scopes)
            {
                // trim the scope part
                if (name.IndexOf(scope, StringComparison.OrdinalIgnoreCase) == 0)

                {
                    return name.Substring(scope.Length);
                }
            }

            // no scope
            return name;
        }

        /// <summary>
        /// Given a function name, strip the scope of the name
        /// </summary>
        /// <param name="functionName"></param>
        /// <returns></returns>
        public string FunctionNameWithoutScope(string functionName)
        {
            return NameWithoutScope(functionName, functionScopes);
        }

        /// <summary>
        /// Given a variable name, strip the scope
        /// </summary>
        /// <param name="variableName"></param>
        /// <returns></returns>
        public string VariableNameWithoutScope(VariablePath variablePath)
        {
            if (variablePath == null || variablePath.UserPath == null)
            {
                return null;
            }

            // strip out the drive if there is one
            if (!string.IsNullOrWhiteSpace(variablePath.DriveName)
                // checks that variable starts with drivename:
                && variablePath.UserPath.IndexOf(string.Concat(variablePath.DriveName, ":")) == 0)
            {
                return variablePath.UserPath.Substring(variablePath.DriveName.Length + 1);
            }

            return NameWithoutScope(variablePath.UserPath, variableScopes);
        }

        /// <summary>
        /// Given a commandast, checks whether it uses splatted variable
        /// </summary>
        /// <param name="cmdAst"></param>
        /// <returns></returns>
        public bool HasSplattedVariable(CommandAst cmdAst)
        {
            return cmdAst != null
                && cmdAst.CommandElements != null
                && cmdAst.CommandElements.Any(cmdElem =>
                {
                    var varExprAst = cmdElem as VariableExpressionAst;
                    return varExprAst != null && varExprAst.Splatted;
                });
        }

        /// <summary>
        /// Given a commandast, checks if the command is a known cmdlet, function or ExternalScript. 
        /// </summary>
        /// <param name="cmdAst"></param>
        /// <returns></returns>
        public bool IsKnownCmdletFunctionOrExternalScript(CommandAst cmdAst, out CommandInfo commandInfo)
        {
            commandInfo = null;
            if (cmdAst == null)
            {
                return false;
            }

            commandInfo = GetCommandInfo(cmdAst.GetCommandName());
            if (commandInfo == null)
            {
                return false;
            }

            return commandInfo.CommandType == CommandTypes.Cmdlet ||
                   commandInfo.CommandType == CommandTypes.Alias ||
                   commandInfo.CommandType == CommandTypes.ExternalScript;
        }

        /// <summary>
        /// Given a commandast, checks whether positional parameters are used or not.
        /// </summary>
        /// <param name="cmdAst"></param>
        /// <param name="moreThanTwoPositional">only return true if more than two positional parameters are used</param>
        /// <returns></returns>
        public bool PositionalParameterUsed(CommandAst cmdAst, bool moreThanTwoPositional = false)
        {
            if (HasSplattedVariable(cmdAst))
            {
                return false;
            }

            // Because of the way we count, we will also count the cmdlet as an argument so we have to -1
            int argumentsWithoutProcedingParameters = 0;

            var commandElementCollection = cmdAst.CommandElements;
            for (int i = 1; i < commandElementCollection.Count(); i++) {
                if (!(commandElementCollection[i] is CommandParameterAst) && !(commandElementCollection[i-1] is CommandParameterAst))
                {
                    argumentsWithoutProcedingParameters++;
                }
            }

            // if not the first element in a pipeline, increase the number of arguments by 1
            PipelineAst parent = cmdAst.Parent as PipelineAst;
            if (parent != null && parent.PipelineElements.Count > 1 && parent.PipelineElements[0] != cmdAst)
            {
                argumentsWithoutProcedingParameters++;
            }

            return moreThanTwoPositional ? argumentsWithoutProcedingParameters > 2 : argumentsWithoutProcedingParameters > 0;
        }


        /// <summary>
        /// Given a command's name, checks whether it exists.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="commandType"></param>
        /// <param name="bypassCache"></param>
        /// <returns></returns>
        public CommandInfo GetCommandInfo(string name, CommandTypes? commandType = null, bool bypassCache = false)
        {
            return CommandInfoCache.GetCommandInfo(name, commandTypes: commandType, bypassCache: bypassCache);
        }

        /// <summary>
        /// Returns the get, set and test targetresource dsc function
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public IEnumerable<Ast> DscResourceFunctions(Ast ast)
        {
            List<string> resourceFunctionNames = new List<string>(new string[] { "Set-TargetResource", "Get-TargetResource", "Test-TargetResource" });
            return ast.FindAll(item => item is FunctionDefinitionAst
                && resourceFunctionNames.Contains((item as FunctionDefinitionAst).Name, StringComparer.OrdinalIgnoreCase), true);
        }

        /// <summary>
        /// Gets all the strings contained in an array literal ast
        /// </summary>
        /// <param name="alAst"></param>
        /// <returns></returns>
        public List<string> GetStringsFromArrayLiteral(ArrayLiteralAst alAst)
        {
            List<string> result = new List<string>();

            if (alAst != null && alAst.Elements != null)
            {
                foreach (ExpressionAst eAst in alAst.Elements)
                {
                    if (eAst is StringConstantExpressionAst)
                    {
                        result.Add((eAst as StringConstantExpressionAst).Value);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns true if the block should be skipped as it has a name
        /// that matches keyword
        /// </summary>
        /// <param name="keyword"></param>
        /// <param name="namedBlockAst"></param>
        /// <returns></returns>
        public bool SkipBlock(string keyword, Ast namedBlockAst)
        {
            if (namedBlockAst == null)
            {
                return false;
            }

            FindClosingParenthesis(keyword);

            List<Tuple<int, int>> listTuples = KeywordBlockDictionary[keyword];

            if (listTuples == null || listTuples.Count == 0)
            {
                return false;
            }

            int index = listTuples.BinarySearch(Tuple.Create(namedBlockAst.Extent.StartOffset, namedBlockAst.Extent.EndOffset), tupleComparer);

            if (index < 0 || index >= Tokens.Length)
            {
                return false;
            }

            Tuple<int, int> braces = listTuples[index];

            if (braces.Item2 == namedBlockAst.Extent.EndOffset)
            {
                return true;
            }

            return false;
        }

        // Obtain script extent for the function - just around the function name
        public IScriptExtent GetScriptExtentForFunctionName(FunctionDefinitionAst functionDefinitionAst)
        {
            if (null == functionDefinitionAst)
            {
                return null;
            }
            var funcNameTokens = Tokens.Where(
                token =>
                ContainsExtent(functionDefinitionAst.Extent, token.Extent)
                && token.Text.Equals(functionDefinitionAst.Name));
            var funcNameToken = funcNameTokens.FirstOrDefault();
            return funcNameToken == null ? null : funcNameToken.Extent;
        }

        /// <summary>
        /// Return true if subset is contained in set
        /// </summary>
        /// <param name="set"></param>
        /// <param name="subset"></param>
        /// <returns>True or False</returns>
        public static bool ContainsExtent(IScriptExtent set, IScriptExtent subset)
        {
            if (set == null || subset == null)
            {
                return false;
            }
            return set.StartOffset <= subset.StartOffset
                && set.EndOffset >= subset.EndOffset;
        }
        private void FindClosingParenthesis(string keyword)
        {
            if (Tokens == null || Tokens.Length == 0)
            {
                return;
            }

            // Only do this one time per script. The keywordblockdictionary is cleared everytime we run a new script
            if (KeywordBlockDictionary.ContainsKey(keyword))
            {
                return;
            }

            KeywordBlockDictionary[keyword] = new List<Tuple<int, int>>();

            int[] tokenIndices = Tokens
                .Select((token, index) =>
                    String.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase) && (token.TokenFlags == TokenFlags.Keyword || token.TokenFlags == TokenFlags.CommandName)
                    ? index : -1)
                .Where(index => index != -1).ToArray();

            foreach (int tokenIndex in tokenIndices)
            {
                int openCurly = -1;

                for (int i = tokenIndex; i < Tokens.Length; i += 1)
                {
                    if (Tokens[i] != null && Tokens[i].Kind == TokenKind.LCurly)
                    {
                        openCurly = i;
                        break;
                    }
                }

                if (openCurly == -1)
                {
                    continue;
                }

                int closeCurly = -1;
                int count = 1;

                for (int i = openCurly + 1; i < Tokens.Length; i += 1)
                {
                    if (Tokens[i] != null)
                    {
                        if (Tokens[i].Kind == TokenKind.LCurly)
                        {
                            count += 1;
                        }
                        else if (Tokens[i].Kind == TokenKind.RCurly)
                        {
                            count -= 1;
                        }
                    }

                    if (count == 0)
                    {
                        closeCurly = i;
                        break;
                    }
                }

                if (closeCurly == -1)
                {
                    continue;
                }

                KeywordBlockDictionary[keyword].Add(Tuple.Create(Tokens[openCurly].Extent.StartOffset,
                    Tokens[closeCurly].Extent.EndOffset));
            }
        }

        /// <summary>
        /// Checks whether the variable VarAst is uninitialized.
        /// </summary>
        /// <param name="varAst"></param>
        /// <param name="ast"></param>
        /// <returns></returns>
        public bool IsUninitialized(VariableExpressionAst varAst, Ast ast)
        {
            if (!VariableAnalysisDictionary.ContainsKey(ast) || VariableAnalysisDictionary[ast] == null)
            {
                return false;
            }

            return VariableAnalysisDictionary[ast].IsUninitialized(varAst);
        }

        /// <summary>
        /// Returns true if variable is either a global variable or an environment variable
        /// </summary>
        /// <param name="varAst"></param>
        /// <returns></returns>
        public bool IsVariableGlobalOrEnvironment(VariableExpressionAst varAst)
        {
            return VariableAnalysis.IsGlobalOrEnvironment(varAst);
        }


        /// <summary>
        /// Checks whether a variable is a global variable.
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public bool IsVariableGlobal(VariableExpressionAst varAst)
        {
            //We ignore the use of built-in variable as global variable
            if (varAst.VariablePath.IsGlobal)
            {
                string varName = varAst.VariablePath.UserPath.Remove(varAst.VariablePath.UserPath.IndexOf("global:", StringComparison.OrdinalIgnoreCase), "global:".Length);
                return !SpecialVars.InitializedVariables.Contains(varName, StringComparer.OrdinalIgnoreCase);
            }
            return false;
        }


        /// <summary>
        /// Checks whether all the code path of ast returns.
        /// Runs InitializeVariableAnalysis before calling this method
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public bool AllCodePathReturns(Ast ast)
        {
            if (!VariableAnalysisDictionary.ContainsKey(ast))
            {
                return true;
            }

            var analysis = VariableAnalysisDictionary[ast];
            return analysis.Exit._predecessors.All(block => block._returns || block._unreachable || block._throws);
        }

        /// <summary>
        /// Initialize variable analysis on the script ast
        /// </summary>
        /// <param name="ast"></param>
        public void InitializeVariableAnalysis(Ast ast)
        {
            (new ScriptAnalysis()).AnalyzeScript(ast);
        }

        /// <summary>
        /// Initialize Variable Analysis on Ast ast with variables outside in outerAnalysis
        /// </summary>
        /// <param name="ast"></param>
        internal VariableAnalysis InitializeVariableAnalysisHelper(Ast ast, VariableAnalysis outerAnalysis)
        {
            var VarAnalysis = new VariableAnalysis(new FlowGraph());
            VarAnalysis.AnalyzeImpl(ast, outerAnalysis);
            VariableAnalysisDictionary[ast] = VarAnalysis;
            return VarAnalysis;
        }

        /// <summary>
        /// Get the return type of ret, which is used in function funcAst in scriptAst ast
        /// This function assumes that initialize variable analysis is already run on funcast
        /// It also assumes that the pipeline of ret is not null
        /// </summary>
        /// <param name="funcAst"></param>
        /// <param name="ret"></param>
        /// <param name="classes"></param>
        /// <param name="scriptAst"></param>
        /// <returns></returns>

#if (PSV3||PSV4)

        public string GetTypeFromReturnStatementAst(Ast funcAst, ReturnStatementAst ret)

#else

        public string GetTypeFromReturnStatementAst(Ast funcAst, ReturnStatementAst ret, IEnumerable<TypeDefinitionAst> classes)

#endif
        {
            if (ret == null || funcAst == null)
            {
                return String.Empty;
            }

            PipelineAst pipe = ret.Pipeline as PipelineAst;

            String result = String.Empty;

            // Handle the case with 1 pipeline element first
            if (pipe != null && pipe.PipelineElements.Count == 1)
            {
                CommandExpressionAst cmAst = pipe.PipelineElements[0] as CommandExpressionAst;
                if (cmAst != null)
                {
                    if (cmAst.Expression.StaticType != typeof(object))
                    {
                        result = cmAst.Expression.StaticType.FullName;
                    }
                    else
                    {
                        VariableExpressionAst varAst = cmAst.Expression as VariableExpressionAst;

                        if (varAst != null)
                        {
                            result = GetVariableTypeFromAnalysis(varAst, funcAst);
                        }
                        else if (cmAst.Expression is MemberExpressionAst)
                        {
#if PSV3

                            result = GetTypeFromMemberExpressionAst(cmAst.Expression as MemberExpressionAst, funcAst);

#else

                            result = GetTypeFromMemberExpressionAst(cmAst.Expression as MemberExpressionAst, funcAst, classes);

#endif
                        }
                    }
                }
            }

            if (String.IsNullOrWhiteSpace(result) && pipe != null && pipe.PipelineElements.Count > 0)
            {
                result = typeof(object).FullName;
            }

            return result;
        }

        /// <summary>
        /// Returns the type from member expression ast, which is inside scopeAst.
        /// This function assumes that Initialize Variable Analysis is already run on scopeAst.
        /// Classes represent the list of DSC classes in the script.
        /// </summary>
        /// <param name="memberAst"></param>
        /// <param name="scopeAst"></param>
        /// <param name="classes"></param>
        /// <returns></returns>

#if (PSV3||PSV4)

        public string GetTypeFromMemberExpressionAst(MemberExpressionAst memberAst, Ast scopeAst)

#else

        public string GetTypeFromMemberExpressionAst(MemberExpressionAst memberAst, Ast scopeAst, IEnumerable<TypeDefinitionAst> classes)

#endif
        {
            if (memberAst == null)
            {
                return String.Empty;
            }

            VariableAnalysisDetails details = null;

#if !(PSV3||PSV4)

            TypeDefinitionAst psClass = null;

#endif

            if (memberAst.Expression is VariableExpressionAst && VariableAnalysisDictionary.ContainsKey(scopeAst))
            {
                VariableAnalysis VarTypeAnalysis = VariableAnalysisDictionary[scopeAst];
                // Get the analysis detail for the variable
                details = VarTypeAnalysis.GetVariableAnalysis(memberAst.Expression as VariableExpressionAst);

#if !PSV3

                if (details != null && classes != null)
                {
                    // Get the class that corresponds to the name of the type (if possible, the type is not available in the case of a static Singleton)
                    psClass = classes.FirstOrDefault(item => String.Equals(item.Name, details.Type?.FullName, StringComparison.OrdinalIgnoreCase));
                }

#endif
            }

#if PSV3

                return GetTypeFromMemberExpressionAstHelper(memberAst, details);

#else

                return GetTypeFromMemberExpressionAstHelper(memberAst, psClass, details);

#endif
        }

        /// <summary>
        /// Retrieves the type from member expression ast. psClass is the powershell class
        /// that represents the type of the object being invoked on (psClass may be null too).
        /// </summary>
        /// <param name="memberAst"></param>
        /// <param name="psClass"></param>
        /// <param name="analysisDetails"></param>
        /// <returns></returns>

#if (PSV3||PSV4)

        internal string GetTypeFromMemberExpressionAstHelper(MemberExpressionAst memberAst, VariableAnalysisDetails analysisDetails)

#else

        internal string GetTypeFromMemberExpressionAstHelper(MemberExpressionAst memberAst, TypeDefinitionAst psClass, VariableAnalysisDetails analysisDetails)

#endif
        {
            //Try to get the type without using psClass first
            Type result = AssignmentTarget.GetTypeFromMemberExpressionAst(memberAst);

#if !(PSV3||PSV4)

            //If we can't get the type, then it may be that the type of the object being invoked on is a powershell class
            if (result == null && psClass != null && analysisDetails != null)
            {
                result = AssignmentTarget.GetTypeFromMemberExpressionAst(memberAst, analysisDetails, psClass);
            }

#endif

            if (result != null)
            {
                return result.FullName;
            }

            return String.Empty;
        }

        /// <summary>
        /// Get the type of varAst
        /// </summary>
        /// <param name="varAst"></param>
        /// <param name="ast"></param>
        /// <returns></returns>
        public Type GetTypeFromAnalysis(VariableExpressionAst varAst, Ast ast)
        {
            try
            {
                if (VariableAnalysisDictionary.ContainsKey(ast))
                {
                    VariableAnalysis VarTypeAnalysis = VariableAnalysisDictionary[ast];
                    VariableAnalysisDetails details = VarTypeAnalysis.GetVariableAnalysis(varAst);
                    return details.Type;
                }
                else
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get type of variable from the variable analysis
        /// </summary>
        /// <param name="varAst"></param>
        /// <param name="ast"></param>
        public string GetVariableTypeFromAnalysis(VariableExpressionAst varAst, Ast ast)
        {
            Type result = GetTypeFromAnalysis(varAst, ast);
            if (result != null)
            {
                return result.FullName;
            }

            return String.Empty;
        }

        /// <summary>
        /// Checks whether the cmdlet parameter is a PS default variable
        /// </summary>
        /// <param name="varName"></param>
        /// <returns></returns>
        public bool HasSpecialVars(string varName)
        {
            if (SpecialVars.InitializedVariables.Contains(varName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns a dictionary of rule suppression from the ast.
        /// Key of the dictionary is rule name.
        /// Value is a list of tuple of integers that represents the interval to apply the rule
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public Dictionary<string, List<RuleSuppression>> GetRuleSuppression(Ast ast)
        {
            List<RuleSuppression> ruleSuppressionList = new List<RuleSuppression>();
            Dictionary<string, List<RuleSuppression>> results = new Dictionary<string, List<RuleSuppression>>(StringComparer.OrdinalIgnoreCase);

            if (ast == null)
            {
                return results;
            }

            ScriptBlockAst sbAst = ast as ScriptBlockAst;

            // Get rule suppression from the ast itself if it is scriptblockast
            if (sbAst != null && sbAst.ParamBlock != null && sbAst.ParamBlock.Attributes != null)
            {
                ruleSuppressionList.AddRange(RuleSuppression.GetSuppressions(sbAst.ParamBlock.Attributes, sbAst.Extent.StartOffset, sbAst.Extent.EndOffset, sbAst));
            }

            // Get rule suppression from functions
            IEnumerable<FunctionDefinitionAst> funcAsts = ast.FindAll(item => item is FunctionDefinitionAst, true).Cast<FunctionDefinitionAst>();

            foreach (var funcAst in funcAsts)
            {
                ruleSuppressionList.AddRange(GetSuppressionsFunction(funcAst));
            }

#if !(PSV3||PSV4)
            // Get rule suppression from classes
            IEnumerable<TypeDefinitionAst> typeAsts = ast.FindAll(item => item is TypeDefinitionAst, true).Cast<TypeDefinitionAst>();

            foreach (var typeAst in typeAsts)
            {
                ruleSuppressionList.AddRange(GetSuppressionsClass(typeAst));
            }

            // Get rule suppression from configuration definitions
            IEnumerable<ConfigurationDefinitionAst> configDefAsts = ast.FindAll(item => item is ConfigurationDefinitionAst, true).Cast<ConfigurationDefinitionAst>();

            foreach (var configDefAst in configDefAsts)
            {
                ruleSuppressionList.AddRange(GetSuppressionsConfiguration(configDefAst));
            }
#endif // !PSV3

            ruleSuppressionList.Sort((item, item2) => item.StartOffset.CompareTo(item2.StartOffset));

            foreach (RuleSuppression ruleSuppression in ruleSuppressionList)
            {
                if (!results.ContainsKey(ruleSuppression.RuleName))
                {
                    List<RuleSuppression> ruleSuppressions = new List<RuleSuppression>();
                    results.Add(ruleSuppression.RuleName, ruleSuppressions);
                }

                results[ruleSuppression.RuleName].Add(ruleSuppression);
            }

            return results;
        }

        /// <summary>
        /// Returns a list of rule suppressions from the function
        /// </summary>
        /// <param name="funcAst"></param>
        /// <returns></returns>
        internal List<RuleSuppression> GetSuppressionsFunction(FunctionDefinitionAst funcAst)
        {
            List<RuleSuppression> result = new List<RuleSuppression>();

            if (funcAst != null && funcAst.Body != null
                && funcAst.Body.ParamBlock != null && funcAst.Body.ParamBlock.Attributes != null)
            {
                result.AddRange(RuleSuppression.GetSuppressions(funcAst.Body.ParamBlock.Attributes, funcAst.Extent.StartOffset, funcAst.Extent.EndOffset, funcAst));
            }

            return result;
        }

#if !(PSV3||PSV4)
        /// <summary>
        /// Returns a list of rule suppression from the class
        /// </summary>
        /// <param name="typeAst"></param>
        /// <returns></returns>
        internal List<RuleSuppression> GetSuppressionsClass(TypeDefinitionAst typeAst)
        {
            List<RuleSuppression> result = new List<RuleSuppression>();

            if (typeAst != null && typeAst.Attributes != null && typeAst.Attributes.Count != 0)
            {
                result.AddRange(RuleSuppression.GetSuppressions(typeAst.Attributes, typeAst.Extent.StartOffset, typeAst.Extent.EndOffset, typeAst));
            }

            if (typeAst.Members == null)
            {
                return result;
            }

            foreach (var member in typeAst.Members)
            {

                FunctionMemberAst funcMemb = member as FunctionMemberAst;
                if (funcMemb == null)
                {
                    continue;
                }

                result.AddRange(RuleSuppression.GetSuppressions(funcMemb.Attributes, funcMemb.Extent.StartOffset, funcMemb.Extent.EndOffset, funcMemb));
            }

            return result;
        }

        /// <summary>
        /// Returns a list of rule suppressions from the configuration
        /// </summary>
        /// <param name="configDefAst"></param>
        /// <returns></returns>
        internal List<RuleSuppression> GetSuppressionsConfiguration(ConfigurationDefinitionAst configDefAst)
        {
            var result = new List<RuleSuppression>();
            if (configDefAst == null || configDefAst.Body == null)
            {
                return result;
            }
            var attributeAsts = configDefAst.FindAll(x => x is AttributeAst, true).Cast<AttributeAst>();
            result.AddRange(RuleSuppression.GetSuppressions(
                attributeAsts,
                configDefAst.Extent.StartOffset,
                configDefAst.Extent.EndOffset,
                configDefAst));
            return result;
        }

#endif // !PSV3

        /// <summary>
        /// Suppress the rules from the diagnostic records list.
        /// Returns a list of suppressed records as well as the ones that are not suppressed
        /// </summary>
        /// <param name="ruleName">The name of the rule possibly being suppressed.</param>
        /// <param name="ruleSuppressionsDict">A lookup table from rule name to suppressions of that rule.</param>
        /// <param name="diagnostics">The list of all diagnostics emitted by the given rule.</param>
        /// <param name="errors">Any errors that arise due to misconfigured suppression settings.</param>
        /// <returns>A pair, with the first item being the list of suppressed diagnostics, and the second the list of unsuppressed diagnostics.</returns>
        public Tuple<List<SuppressedRecord>, List<DiagnosticRecord>> SuppressRule(
            string ruleName,
            Dictionary<string, List<RuleSuppression>> ruleSuppressionsDict,
            List<DiagnosticRecord> diagnostics,
            out List<ErrorRecord> errors)
        {
            var suppressedRecords = new List<SuppressedRecord>();
            var unsuppressedRecords = new List<DiagnosticRecord>();
            errors = new List<ErrorRecord>();
            var result = new Tuple<List<SuppressedRecord>, List<DiagnosticRecord>>(suppressedRecords, unsuppressedRecords);

            if (diagnostics is null || diagnostics.Count == 0)
            {
                return result;
            }

            if (ruleSuppressionsDict is null
                || !ruleSuppressionsDict.TryGetValue(ruleName, out List<RuleSuppression> ruleSuppressions)
                || ruleSuppressions.Count == 0)
            {
                unsuppressedRecords.AddRange(diagnostics);
                return result;
            }

            // We need to report errors for any rule suppressions with IDs that are not applied to anything
            // Start by assuming all suppressions are unapplied and then we'll remove them as we apply them later
            var unappliedSuppressions = new HashSet<RuleSuppression>();
            foreach (RuleSuppression suppression in ruleSuppressions)
            {
                if (!string.IsNullOrWhiteSpace(suppression.RuleSuppressionID))
                {
                    unappliedSuppressions.Add(suppression);
                }
            }
            
            // We have suppressions and diagnostics
            // For each diagnostic, collect all the suppressions that apply to it
            // and if there are any, record the diagnostic as suppressed
            foreach (DiagnosticRecord diagnostic in diagnostics)
            {
                Tuple<int, int> offsetPair = GetOffset(diagnostic);
                
                var appliedSuppressions = new List<RuleSuppression>();
                foreach (RuleSuppression suppression in ruleSuppressions)
                {
                    // Check that the diagnostic is within the suppressed extent, if we have such an extent
                    if (!(offsetPair is null)
                        && (offsetPair.Item1 < suppression.StartOffset || offsetPair.Item2 > suppression.EndOffset))
                    {
                        continue;
                    }
                    
                    // If there's a rule suppression ID, check that it matches the diagnostic
                    if (!string.IsNullOrWhiteSpace(suppression.RuleSuppressionID)
                        && !string.Equals(diagnostic.RuleSuppressionID, suppression.RuleSuppressionID, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    unappliedSuppressions.Remove(suppression);
                    appliedSuppressions.Add(suppression);
                }
                
                if (appliedSuppressions.Count > 0)
                {
                    suppressedRecords.Add(new SuppressedRecord(diagnostic, appliedSuppressions));
                }
                else
                {
                    unsuppressedRecords.Add(diagnostic);
                }
            }
            
            // Do any error reporting for misused RuleSuppressionIDs here.
            // If the user applied a suppression qualified by a suppression ID,
            // but that suppression didn't apply only because the suppression ID didn't match
            // we let the user know in the form of an error.
            string analyzedFile = diagnostics[0]?.Extent?.File;
            bool appliedToFile = !string.IsNullOrEmpty(analyzedFile);
            string analyzedFileName = appliedToFile ? Path.GetFileName(analyzedFile) : null;
            foreach (RuleSuppression unappliedSuppression in unappliedSuppressions)
            {
                string suppressionIDError = string.Format(Strings.RuleSuppressionIDError, unappliedSuppression.RuleSuppressionID);

                if (appliedToFile)
                {
                    unappliedSuppression.Error = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.RuleSuppressionErrorFormat,
                        unappliedSuppression.StartAttributeLine,
                        analyzedFileName,
                        suppressionIDError);
                }
                else
                {
                    unappliedSuppression.Error = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.RuleSuppressionErrorFormatScriptDefinition,
                        unappliedSuppression.StartAttributeLine,
                        suppressionIDError);
                }
                
                errors.Add(
                    new ErrorRecord(
                        new ArgumentException(unappliedSuppression.Error),
                        unappliedSuppression.Error,
                        ErrorCategory.InvalidArgument,
                        unappliedSuppression));
            }

            return result;
        }

        private Tuple<int, int> GetOffset(DiagnosticRecord diagnostic)
        {
            IScriptExtent extent = diagnostic.Extent;

            if (extent is null)
            {
                return null;
            }

            if (extent.StartOffset != 0 && extent.EndOffset != 0)
            {
                return Tuple.Create(extent.StartOffset, extent.EndOffset);
            }

            // We're forced to determine the offset ourselves from the lines and columns now
            // Rather than counting every line in the file, we scan through the tokens looking for ones matching the offset

            Token startToken = null;
            int i = 0;
            for (; i < Tokens.Length; i++)
            {
                Token curr = Tokens[i];
                if (curr.Extent.StartLineNumber == extent.StartLineNumber
                    && curr.Extent.StartColumnNumber == extent.StartColumnNumber)
                {
                    startToken = curr;
                    break;
                }
            }

            if (startToken is null)
            {
                return Tuple.Create(0, 0);
            }

            Token endToken = null;
            for (; i < Tokens.Length; i++)
            {
                Token curr = Tokens[i];
                if (curr.Extent.EndLineNumber == extent.EndLineNumber
                    && curr.Extent.EndColumnNumber == extent.EndColumnNumber)
                {
                    endToken = curr;
                    break;
                }
            }

            if (endToken is null)
            {
                return Tuple.Create(0, 0);
            }

            return Tuple.Create(startToken.Extent.StartOffset, endToken.Extent.EndOffset);
        }

        public static string[] ProcessCustomRulePaths(string[] rulePaths, SessionState sessionState, bool recurse = false)
        {
            //if directory is given, list all the psd1 files
            List<string> outPaths = new List<string>();
            if (rulePaths == null)
            {
                return null;
            }

            Collection<PathInfo> pathInfo = new Collection<PathInfo>();
            foreach (string rulePath in rulePaths)
            {
                Collection<PathInfo> pathInfosForRulePath = sessionState.Path.GetResolvedPSPathFromPSPath(rulePath);
                if (null != pathInfosForRulePath)
                {
                    foreach (PathInfo pathInfoForRulePath in pathInfosForRulePath)
                    {
                        pathInfo.Add(pathInfoForRulePath);
                    }
                }
            }

            foreach (PathInfo pinfo in pathInfo)
            {
                string path = pinfo.Path;
                if (Directory.Exists(path))
                {
                    path = path.TrimEnd('\\');
                    if (recurse)
                    {
                        outPaths.AddRange(Directory.GetDirectories(pinfo.Path, "*", SearchOption.AllDirectories));
                    }
                }
                outPaths.Add(path);
            }

            return outPaths.ToArray();

        }

        /// <summary>
        /// Check if the function name starts with one of potentailly state changing verbs
        /// </summary>
        /// <param name="functionName"></param>
        /// <returns>true if the function name starts with a state changing verb, otherwise false</returns>
        public bool IsStateChangingFunctionName(string functionName)
        {
            if (functionName == null)
            {
                throw new ArgumentNullException("functionName");
            }
            // Array of verbs that can potentially change the state of a system
            string[] stateChangingVerbs =
            {
                "New-",
                "Set-",
                "Remove-",
                "Start-",
                "Stop-",
                "Restart-",
                "Reset-",
                "Update-"
            };
            foreach (var verb in stateChangingVerbs)
            {
                if (functionName.StartsWith(verb, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get the SupportShouldProcess attribute ast
        /// </summary>
        /// <param name="attributeAsts"></param>
        /// <returns>Returns SupportShouldProcess attribute ast if it exists, otherwise returns null</returns>
        public NamedAttributeArgumentAst GetShouldProcessAttributeAst(IEnumerable<AttributeAst> attributeAsts)
        {
            if (attributeAsts == null)
            {
                throw new ArgumentNullException("attributeAsts");
            }
            var cmdletBindingAttributeAst = this.GetCmdletBindingAttributeAst(attributeAsts);
            if (cmdletBindingAttributeAst == null
                || cmdletBindingAttributeAst.NamedArguments == null)
            {
                return null;
            }
            foreach (var namedAttributeAst in cmdletBindingAttributeAst.NamedArguments)
            {
                if (namedAttributeAst != null
                    && namedAttributeAst.ArgumentName.Equals(
                        "SupportsShouldProcess",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return namedAttributeAst;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the CmdletBinding attribute ast
        /// </summary>
        /// <param name="attributeAsts"></param>
        /// <returns>Returns CmdletBinding attribute ast if it exists, otherwise returns null</returns>
        public AttributeAst GetCmdletBindingAttributeAst(IEnumerable<AttributeAst> attributeAsts)
        {
            if (attributeAsts == null)
            {
                throw new ArgumentNullException("attributeAsts");
            }
            foreach (var attributeAst in attributeAsts)
            {
                if (attributeAst == null || attributeAst.NamedArguments == null)
                {
                    continue;
                }
                if (attributeAst.TypeName.GetReflectionAttributeType()
                    == typeof(CmdletBindingAttribute))
                {
                    return attributeAst;
                }
            }
            return null;
        }

        /// <summary>
        /// Get the boolean value of the named attribute argument
        /// </summary>
        /// <param name="namedAttributeArgumentAst"></param>
        /// <returns>Boolean value of the named attribute argument</returns>
        public bool GetNamedArgumentAttributeValue(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            if (namedAttributeArgumentAst == null)
            {
                throw new ArgumentNullException("namedAttributeArgumentAst");
            }
            if (namedAttributeArgumentAst.ExpressionOmitted)
            {
                return true;
            }
            else
            {
                var varExpAst = namedAttributeArgumentAst.Argument as VariableExpressionAst;
                if (varExpAst == null)
                {
                    var constExpAst = namedAttributeArgumentAst.Argument as ConstantExpressionAst;
                    if (constExpAst == null)
                    {
                        return false;
                    }
                    bool constExpVal;
                    if (LanguagePrimitives.TryConvertTo<bool>(constExpAst.Value, out constExpVal))
                    {
                        return constExpVal;
                    }
                }
                else
                {
                    return varExpAst.VariablePath.UserPath.Equals(
                        bool.TrueString,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        /// <summary>
        /// Gets valid keys of a PowerShell module manifest file for a given PowerShell version
        /// </summary>
        /// <param name="powershellVersion">Version parameter; valid if >= 3.0</param>
        /// <returns>Returns an enumerator over valid keys</returns>
        public static IEnumerable<string> GetModuleManifestKeys(Version powershellVersion)
        {
            if (powershellVersion == null)
            {
                throw new ArgumentNullException("powershellVersion");
            }
            if (!IsPowerShellVersionSupported(powershellVersion))
            {
                throw new ArgumentException("Invalid PowerShell version. Choose from version greater than or equal to 3.0");
            }
            var keys = new List<string>();
            var keysCommon = new List<string> {
                    "RootModule",
                    "ModuleVersion",
                    "GUID",
                    "Author",
                    "CompanyName",
                    "Copyright",
                    "Description",
                    "PowerShellVersion",
                    "PowerShellHostName",
                    "PowerShellHostVersion",
                    "DotNetFrameworkVersion",
                    "CLRVersion",
                    "ProcessorArchitecture",
                    "RequiredModules",
                    "RequiredAssemblies",
                    "ScriptsToProcess",
                    "TypesToProcess",
                    "FormatsToProcess",
                    "NestedModules",
                    "FunctionsToExport",
                    "CmdletsToExport",
                    "VariablesToExport",
                    "AliasesToExport",
                    "ModuleList",
                    "FileList",
                    "PrivateData",
                    "HelpInfoURI",
                    "DefaultCommandPrefix"};
            keys.AddRange(keysCommon);
            if (powershellVersion.Major >= 5)
            {
                keys.Add("DscResourcesToExport");
            }
            if (powershellVersion >= new Version(5, 1))
            {
                keys.Add("CompatiblePSEditions");
            }
            return keys;
        }

        /// <summary>
        /// Gets deprecated keys of PowerShell module manifest
        /// </summary>
        /// <returns>Returns an enumerator over deprecated keys</returns>
        public static IEnumerable<string> GetDeprecatedModuleManifestKeys()
        {
            return new List<string> { "ModuleToProcess" };
        }

        /// <summary>
        /// Get a mapping between string type keys and StatementAsts from module manifest hashtable ast
        ///
        /// This is a workaround as SafeGetValue is not supported on PS v4 and below.
        /// </summary>
        /// <param name="hast">Hashtable Ast obtained from module manifest</param>
        /// <returns>A dictionary that maps string keys to values of StatementAst type</returns>
        private static Dictionary<string, StatementAst> GetMapFromHashtableAst(HashtableAst hast)
        {
            var map = new Dictionary<string, StatementAst>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in hast.KeyValuePairs)
            {
                var key = pair.Item1 as StringConstantExpressionAst;
                if (key == null)
                {
                    return null;
                }
                map[key.Value] = pair.Item2;
            }
            return map;
        }

        /// <summary>
        /// Checks if the version is supported
        ///
        /// PowerShell versions with Major greater than 3 are supported
        /// </summary>
        /// <param name="version">PowerShell version</param>
        /// <returns>true if the given version is supported else false</returns>
        public static bool IsPowerShellVersionSupported(Version version)
        {
            if (version == null)
            {
                throw new ArgumentNullException("version");
            }
            return version >= minSupportedPSVersion;
        }

        /// <summary>
        /// Determines if analyzing a script module.
        /// </summary>
        /// <returns>True is file name ends with ".psm1"</returns>
        public static bool IsModuleScript(string filepath)
        {
            if (filepath == null)
            {
                throw new ArgumentNullException("filepath");
            }
            return filepath.EndsWith(".psm1");
        }

        /// <summary>
        /// Checks if a given file is a valid PowerShell module manifest
        /// </summary>
        /// <param name="filepath">Path to module manifest</param>
        /// <param name="powershellVersion">Version parameter; valid if >= 3.0</param>
        /// <returns>true if given filepath points to a module manifest, otherwise false</returns>
        public static bool IsModuleManifest(string filepath, Version powershellVersion = null)
        {
            Token[] tokens;
            ParseError[] errors;
            if (filepath == null)
            {
                throw new ArgumentNullException("filepath");
            }
            if (powershellVersion != null
                && !IsPowerShellVersionSupported(powershellVersion))
            {
                return false;
            }
            if (!Path.GetExtension(filepath).Equals(".psd1", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            //using parsefile causes the parser to crash!
            string fileContent = File.ReadAllText(filepath);
            var ast = Parser.ParseInput(fileContent, out tokens, out errors);
            var hast = ast.Find(x => x is HashtableAst, false) as HashtableAst;
            if (hast == null)
            {
                return false;
            }
            var map = GetMapFromHashtableAst(hast);
            var deprecatedKeys = GetDeprecatedModuleManifestKeys();
            IEnumerable<string> allKeys;
            if (powershellVersion != null)
            {
                allKeys = GetModuleManifestKeys(powershellVersion);
            }
            else
            {
                Version version = null;
                if (map.ContainsKey("PowerShellVersion"))
                {
                    var versionStrAst = map["PowerShellVersion"].Find(x => x is StringConstantExpressionAst, false);
                    if (versionStrAst != null)
                    {
                        try
                        {
                            version = new Version((versionStrAst as StringConstantExpressionAst).Value);
                        }
                        catch
                        {
                            // we just ignore if the value is not a valid version
                        }
                    }
                }
                if (version != null
                    && IsPowerShellVersionSupported(version))
                {
                    allKeys = GetModuleManifestKeys(version);
                }
                else
                {
                    // default to version 5.1
                    allKeys = GetModuleManifestKeys(new Version("5.1"));
                }
            }

            // check if the keys given in module manifest are a proper subset of Keys
            return map.Keys.All(x => allKeys.Concat(deprecatedKeys).Contains(x, StringComparer.OrdinalIgnoreCase));
        }

        public void SetPSVersionTable(Hashtable psVersionTable)
        {
            if (psVersionTable == null)
            {
                throw new ArgumentNullException("psVersionTable");
            }

            this.psVersionTable = new PSVersionTable(psVersionTable);
        }

#if CORECLR
        public SemanticVersion GetPSVersion()
#else
        public Version GetPSVersion()
#endif
        {
            return psVersionTable == null ? null : psVersionTable.PSVersion;
        }

        /// <summary>
        /// Evaluates all statically evaluable, side-effect-free expressions under an
        /// expression AST to return a value.
        /// Throws if an expression cannot be safely evaluated.
        /// Attempts to replicate the GetSafeValue() method on PowerShell AST methods from PSv5.
        /// </summary>
        /// <param name="exprAst">The expression AST to try to evaluate.</param>
        /// <returns>The .NET value represented by the PowerShell expression.</returns>
        public static object GetSafeValueFromExpressionAst(ExpressionAst exprAst)
        {
            switch (exprAst)
            {
                case ConstantExpressionAst constExprAst:
                    // Note, this parses top-level command invocations as bareword strings
                    // However, forbidding this causes hashtable parsing to fail
                    // It is probably not worth the complexity to isolate this case
                    return constExprAst.Value;

                case VariableExpressionAst varExprAst:
                    // $true and $false are VariableExpressionAsts, so look for them here
                    switch (varExprAst.VariablePath.UserPath.ToLowerInvariant())
                    {
                        case "true":
                            return true;

                        case "false":
                            return false;

                        case "null":
                            return null;

                        default:
                            throw CreateInvalidDataExceptionFromAst(varExprAst);
                    }

                case ArrayExpressionAst arrExprAst:

                    // Most cases are handled by the inner array handling,
                    // but we may have an empty array
                    if (arrExprAst.SubExpression?.Statements == null)
                    {
                        throw CreateInvalidDataExceptionFromAst(arrExprAst);
                    }

                    if (arrExprAst.SubExpression.Statements.Count == 0)
                    {
                        return new object[0];
                    }

                    var listComponents = new List<object>();
                    // Arrays can either be array expressions (1, 2, 3) or array literals with statements @(1 `n 2 `n 3)
                    // Or they can be a combination of these
                    // We go through each statement (line) in an array and read the whole subarray
                    // This will also mean that @(1; 2) is parsed as an array of two elements, but there's not much point defending against this
                    foreach (StatementAst statement in arrExprAst.SubExpression.Statements)
                    {
                        if (!(statement is PipelineAst pipelineAst))
                        {
                            throw CreateInvalidDataExceptionFromAst(arrExprAst);
                        }

                        ExpressionAst pipelineExpressionAst = pipelineAst.GetPureExpression();
                        if (pipelineExpressionAst == null)
                        {
                            throw CreateInvalidDataExceptionFromAst(arrExprAst);
                        }

                        object arrayValue = GetSafeValueFromExpressionAst(pipelineExpressionAst);
                        // We might hit arrays like @(\n1,2,3\n4,5,6), which the parser sees as two statements containing array expressions
                        if (arrayValue is object[] subArray)
                        {
                            listComponents.AddRange(subArray);
                            continue;
                        }

                        listComponents.Add(arrayValue);
                    }
                    return listComponents.ToArray();


                case ArrayLiteralAst arrLiteralAst:
                    return GetSafeValuesFromArrayAst(arrLiteralAst);

                case HashtableAst hashtableAst:
                    return GetSafeValueFromHashtableAst(hashtableAst);

                default:
                    // Other expression types are too complicated or fundamentally unsafe
                    throw CreateInvalidDataExceptionFromAst(exprAst);
            }
        }

        /// <summary>
        /// Create a hashtable value from a PowerShell AST representing one,
        /// provided that the PowerShell expression is statically evaluable and safe.
        /// </summary>
        /// <param name="hashtableAst">The PowerShell representation of the hashtable value.</param>
        /// <returns>The Hashtable as a hydrated .NET value.</returns>
        internal static Hashtable GetSafeValueFromHashtableAst(HashtableAst hashtableAst)
        {
            if (hashtableAst == null)
            {
                throw new ArgumentNullException(nameof(hashtableAst));
            }

            if (hashtableAst.KeyValuePairs == null)
            {
                throw CreateInvalidDataExceptionFromAst(hashtableAst);
            }

            var hashtable = new Hashtable();
            foreach (Tuple<ExpressionAst, StatementAst> entry in hashtableAst.KeyValuePairs)
            {
                // Get the key
                object key = GetSafeValueFromExpressionAst(entry.Item1);
                if (key == null)
                {
                    throw CreateInvalidDataExceptionFromAst(entry.Item1);
                }

                // Get the value
                ExpressionAst valueExprAst = (entry.Item2 as PipelineAst)?.GetPureExpression();
                if (valueExprAst == null)
                {
                    throw CreateInvalidDataExceptionFromAst(entry.Item2);
                }

                // Add the key/value entry into the hydrated hashtable
                hashtable[key] = GetSafeValueFromExpressionAst(valueExprAst);
            }

            return hashtable;
        }

        /// <summary>
        /// Process a PowerShell array literal with statically evaluable/safe contents
        /// into a .NET value.
        /// </summary>
        /// <param name="arrLiteralAst">The PowerShell array AST to turn into a value.</param>
        /// <returns>The .NET value represented by PowerShell syntax.</returns>
        private static object[] GetSafeValuesFromArrayAst(ArrayLiteralAst arrLiteralAst)
        {
            if (arrLiteralAst == null)
            {
                throw new ArgumentNullException(nameof(arrLiteralAst));
            }

            if (arrLiteralAst.Elements == null)
            {
                throw CreateInvalidDataExceptionFromAst(arrLiteralAst);
            }

            var elements = new List<object>();
            foreach (ExpressionAst exprAst in arrLiteralAst.Elements)
            {
                elements.Add(GetSafeValueFromExpressionAst(exprAst));
            }

            return elements.ToArray();
        }

        private static InvalidDataException CreateInvalidDataExceptionFromAst(Ast ast)
        {
            if (ast == null)
            {
                throw new ArgumentNullException(nameof(ast));
            }

            return CreateInvalidDataException(ast.Extent);
        }

        private static InvalidDataException CreateInvalidDataException(IScriptExtent extent)
        {
            return new InvalidDataException(string.Format(
                                    CultureInfo.CurrentCulture,
                                    Strings.WrongValueFormat,
                                    extent.StartLineNumber,
                                    extent.StartColumnNumber,
                                    extent.File ?? ""));
        }


#endregion Methods
    }


    internal class TupleComparer : IComparer<Tuple<int, int>>
    {
        public int Compare(Tuple<int, int> t1, Tuple<int, int> t2)
        {
            if (t1 == null)
            {
                if (t2 == null)
                {
                    return 0;
                }

                return -1;
            }
            else
            {
                if (t2 == null)
                {
                    return 1;
                }
                else
                {
                    return t1.Item1.CompareTo(t2.Item1);
                }
            }
        }
    }

    /// <summary>
    /// Class used to do variable analysis on the whole script
    /// </summary>
    public class ScriptAnalysis : ICustomAstVisitor
    {
        private VariableAnalysis OuterAnalysis;

        /// <summary>
        /// Analyze the script
        /// </summary>
        /// <param name="ast"></param>
        public void AnalyzeScript(Ast ast)
        {
            if (ast != null)
            {
                ast.Visit(this);
            }
        }

        /// <summary>
        /// Visit Script Block Ast. Sets outeranalysis to the ast before visiting others.
        /// </summary>
        /// <param name="scriptBlockAst"></param>
        /// <returns></returns>
        public object VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            if (scriptBlockAst == null) return null;

            VariableAnalysis previousOuter = OuterAnalysis;

            // We already run variable analysis if the parent is a function so skip these.
            // Otherwise, we have to do variable analysis using the outer scope variables.
#if PSV3

                if (!(scriptBlockAst.Parent is FunctionDefinitionAst))

#else

            if (!(scriptBlockAst.Parent is FunctionDefinitionAst) && !(scriptBlockAst.Parent is FunctionMemberAst))

#endif
            {
                OuterAnalysis = Helper.Instance.InitializeVariableAnalysisHelper(scriptBlockAst, OuterAnalysis);
            }

            if (scriptBlockAst.DynamicParamBlock != null)
            {
                scriptBlockAst.DynamicParamBlock.Visit(this);
            }

            if (scriptBlockAst.BeginBlock != null)
            {
                scriptBlockAst.BeginBlock.Visit(this);
            }

            if (scriptBlockAst.ProcessBlock != null)
            {
                scriptBlockAst.ProcessBlock.Visit(this);
            }

            if (scriptBlockAst.EndBlock != null)
            {
                scriptBlockAst.EndBlock.Visit(this);
            }

            VariableAnalysis innerAnalysis = OuterAnalysis;
            OuterAnalysis = previousOuter;

#if PSV3

            if (!(scriptBlockAst.Parent is FunctionDefinitionAst))

#else

            if (!(scriptBlockAst.Parent is FunctionDefinitionAst) && !(scriptBlockAst.Parent is FunctionMemberAst))

#endif
            {
                // Update the variable analysis of the outer script block
                VariableAnalysis.UpdateOuterAnalysis(OuterAnalysis, innerAnalysis);
            }

            return null;
        }

        /// <summary>
        /// perform special visiting action if statement is a typedefinitionast
        /// </summary>
        /// <param name="statementAst"></param>
        /// <returns></returns>
        private object VisitStatementHelper(StatementAst statementAst)
        {
            if (statementAst == null)
            {
                return null;
            }

#if (PSV3||PSV4)

            statementAst.Visit(this);

#else

            TypeDefinitionAst typeAst = statementAst as TypeDefinitionAst;

            if (typeAst == null)
            {
                statementAst.Visit(this);
                return null;
            }

            foreach (var member in typeAst.Members)
            {
                FunctionMemberAst functionMemberAst = member as FunctionMemberAst;

                if (functionMemberAst != null)
                {
                    var previousOuter = OuterAnalysis;
                    OuterAnalysis = Helper.Instance.InitializeVariableAnalysisHelper(functionMemberAst, OuterAnalysis);

                    if (functionMemberAst != null)
                    {
                        functionMemberAst.Body.Visit(this);
                    }

                    OuterAnalysis = previousOuter;
                }
            }

#endif

            return null;
        }

#if !PSV3

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="usingStatement"></param>
        /// <returns></returns>
        public object VisitUsingStatement(UsingStatementAst usingStatement)
        {
            return null;
        }

#endif

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="arrayExpressionAst"></param>
        /// <returns></returns>
        public object VisitArrayExpression(ArrayExpressionAst arrayExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="arrayLiteralAst"></param>
        /// <returns></returns>
        public object VisitArrayLiteral(ArrayLiteralAst arrayLiteralAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="assignmentStatementAst"></param>
        /// <returns></returns>
        public object VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="attributeAst"></param>
        /// <returns></returns>
        public object VisitAttribute(AttributeAst attributeAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="attributedExpressionAst"></param>
        /// <returns></returns>
        public object VisitAttributedExpression(AttributedExpressionAst attributedExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="binaryExpressionAst"></param>
        /// <returns></returns>
        public object VisitBinaryExpression(BinaryExpressionAst binaryExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit body of block statement
        /// </summary>
        /// <param name="blockStatementAst"></param>
        /// <returns></returns>
        public object VisitBlockStatement(BlockStatementAst blockStatementAst)
        {
            if (blockStatementAst != null)
            {
                blockStatementAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="breakStatementAst"></param>
        /// <returns></returns>
        public object VisitBreakStatement(BreakStatementAst breakStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Visits body
        /// </summary>
        /// <param name="catchClauseAst"></param>
        /// <returns></returns>
        public object VisitCatchClause(CatchClauseAst catchClauseAst)
        {
            if (catchClauseAst != null)
            {
                catchClauseAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="commandAst"></param>
        /// <returns></returns>
        public object VisitCommand(CommandAst commandAst)
        {
            if (commandAst == null) return null;

            foreach (CommandElementAst ceAst in commandAst.CommandElements)
            {
                ceAst.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="commandExpressionAst"></param>
        /// <returns></returns>
        public object VisitCommandExpression(CommandExpressionAst commandExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="commandParameterAst"></param>
        /// <returns></returns>
        public object VisitCommandParameter(CommandParameterAst commandParameterAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="constantExpressionAst"></param>
        /// <returns></returns>
        public object VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="continueStatementAst"></param>
        /// <returns></returns>
        public object VisitContinueStatement(ContinueStatementAst continueStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="convertExpressionAst"></param>
        /// <returns></returns>
        public object VisitConvertExpression(ConvertExpressionAst convertExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="dataStatementAst"></param>
        /// <returns></returns>
        public object VisitDataStatement(DataStatementAst dataStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Visit body
        /// </summary>
        /// <param name="doUntilStatementAst"></param>
        /// <returns></returns>
        public object VisitDoUntilStatement(DoUntilStatementAst doUntilStatementAst)
        {
            if (doUntilStatementAst != null)
            {
                doUntilStatementAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit body
        /// </summary>
        /// <param name="doWhileStatementAst"></param>
        /// <returns></returns>
        public object VisitDoWhileStatement(DoWhileStatementAst doWhileStatementAst)
        {
            if (doWhileStatementAst != null)
            {
                doWhileStatementAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="errorExpressionAst"></param>
        /// <returns></returns>
        public object VisitErrorExpression(ErrorExpressionAst errorExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="errorStatementAst"></param>
        /// <returns></returns>
        public object VisitErrorStatement(ErrorStatementAst errorStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="exitStatementAst"></param>
        /// <returns></returns>
        public object VisitExitStatement(ExitStatementAst exitStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="expandableStringExpressionAst"></param>
        /// <returns></returns>
        public object VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="fileRedirectionAst"></param>
        /// <returns></returns>
        public object VisitFileRedirection(FileRedirectionAst fileRedirectionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit body
        /// </summary>
        /// <param name="forEachStatementAst"></param>
        /// <returns></returns>
        public object VisitForEachStatement(ForEachStatementAst forEachStatementAst)
        {
            if (forEachStatementAst != null)
            {
                forEachStatementAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit body
        /// </summary>
        /// <param name="forStatementAst"></param>
        /// <returns></returns>
        public object VisitForStatement(ForStatementAst forStatementAst)
        {
            if (forStatementAst != null)
            {
                forStatementAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Set outer analysis before visiting children
        /// </summary>
        /// <param name="functionDefinitionAst"></param>
        /// <returns></returns>
        public object VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            var outer = OuterAnalysis;
            OuterAnalysis = Helper.Instance.InitializeVariableAnalysisHelper(functionDefinitionAst, OuterAnalysis);

            if (functionDefinitionAst != null)
            {
                functionDefinitionAst.Body.Visit(this);
            }

            OuterAnalysis = outer;
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="hashtableAst"></param>
        /// <returns></returns>
        public object VisitHashtable(HashtableAst hashtableAst)
        {
            return null;
        }

        /// <summary>
        /// Visit the body of each clauses
        /// </summary>
        /// <param name="ifStmtAst"></param>
        /// <returns></returns>
        public object VisitIfStatement(IfStatementAst ifStmtAst)
        {
            if (ifStmtAst != null)
            {
                if (ifStmtAst.Clauses != null)
                {
                    foreach (var clause in ifStmtAst.Clauses)
                    {
                        if (clause.Item2 != null)
                        {
                            clause.Item2.Visit(this);
                        }
                    }
                }

                if (ifStmtAst.ElseClause != null)
                {
                    ifStmtAst.ElseClause.Visit(this);
                }
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="indexExpressionAst"></param>
        /// <returns></returns>
        public object VisitIndexExpression(IndexExpressionAst indexExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="invokeMemberExpressionAst"></param>
        /// <returns></returns>
        public object VisitInvokeMemberExpression(InvokeMemberExpressionAst invokeMemberExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="memberExpressionAst"></param>
        /// <returns></returns>
        public object VisitMemberExpression(MemberExpressionAst memberExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="mergingRedirectionAst"></param>
        /// <returns></returns>
        public object VisitMergingRedirection(MergingRedirectionAst mergingRedirectionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="namedAttributeArgumentAst"></param>
        /// <returns></returns>
        public object VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAttributeArgumentAst)
        {
            return null;
        }

        /// <summary>
        /// Visit each statement
        /// </summary>
        /// <param name="namedBlockAst"></param>
        /// <returns></returns>
        public object VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            if (namedBlockAst != null)
            {
                foreach (var statement in namedBlockAst.Statements)
                {
                    VisitStatementHelper(statement);
                }
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="paramBlockAst"></param>
        /// <returns></returns>
        public object VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="parameterAst"></param>
        /// <returns></returns>
        public object VisitParameter(ParameterAst parameterAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="parenExpressionAst"></param>
        /// <returns></returns>
        public object VisitParenExpression(ParenExpressionAst parenExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit pipeline
        /// </summary>
        /// <param name="pipelineAst"></param>
        /// <returns></returns>
        public object VisitPipeline(PipelineAst pipelineAst)
        {
            if (pipelineAst == null) return null;

            foreach (var command in pipelineAst.PipelineElements)
            {
                command.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="returnStatementAst"></param>
        /// <returns></returns>
        public object VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Visit the scriptblock
        /// </summary>
        /// <param name="scriptBlockExpressionAst"></param>
        /// <returns></returns>
        public object VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockExpressionAst)
        {
            if (scriptBlockExpressionAst != null)
            {
                scriptBlockExpressionAst.ScriptBlock.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit each statement
        /// </summary>
        /// <param name="statementBlockAst"></param>
        /// <returns></returns>
        public object VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            if (statementBlockAst != null)
            {
                foreach (var statement in statementBlockAst.Statements)
                {
                    VisitStatementHelper(statement);
                }
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="stringConstantExpressionAst"></param>
        /// <returns></returns>
        public object VisitStringConstantExpression(StringConstantExpressionAst stringConstantExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="subExpressionAst"></param>
        /// <returns></returns>
        public object VisitSubExpression(SubExpressionAst subExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit the body of each clause
        /// </summary>
        /// <param name="switchStatementAst"></param>
        /// <returns></returns>
        public object VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            if (switchStatementAst != null)
            {
                foreach (var clause in switchStatementAst.Clauses)
                {
                    if (clause.Item2 != null)
                    {
                        clause.Item2.Visit(this);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="throwStatementAst"></param>
        /// <returns></returns>
        public object VisitThrowStatement(ThrowStatementAst throwStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="trapStatementAst"></param>
        /// <returns></returns>
        public object VisitTrap(TrapStatementAst trapStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Visit body, catch and finally
        /// </summary>
        /// <param name="tryStatementAst"></param>
        /// <returns></returns>
        public object VisitTryStatement(TryStatementAst tryStatementAst)
        {
            if (tryStatementAst != null)
            {
                tryStatementAst.Body.Visit(this);

                if (tryStatementAst.CatchClauses != null)
                {
                    foreach (var clause in tryStatementAst.CatchClauses)
                    {
                        clause.Visit(this);
                    }
                }

                if (tryStatementAst.Finally != null)
                {
                    tryStatementAst.Finally.Visit(this);
                }
            }

            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="typeConstraintAst"></param>
        /// <returns></returns>
        public object VisitTypeConstraint(TypeConstraintAst typeConstraintAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="typeExpressionAst"></param>
        /// <returns></returns>
        public object VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="unaryExpressionAst"></param>
        /// <returns></returns>
        public object VisitUnaryExpression(UnaryExpressionAst unaryExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="usingExpressionAst"></param>
        /// <returns></returns>
        public object VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Do nothing
        /// </summary>
        /// <param name="variableExpressionAst"></param>
        /// <returns></returns>
        public object VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit body
        /// </summary>
        /// <param name="whileStatementAst"></param>
        /// <returns></returns>
        public object VisitWhileStatement(WhileStatementAst whileStatementAst)
        {
            if (whileStatementAst != null)
            {
                whileStatementAst.Body.Visit(this);
            }

            return null;
        }
    }

    /// <summary>
    /// This class is used to find elements in outputted in pipeline.
    /// </summary>
    public class FindPipelineOutput : ICustomAstVisitor
    {
        List<Tuple<string, StatementAst>> outputTypes;

#if !(PSV3||PSV4)

        IEnumerable<TypeDefinitionAst> classes;

#endif

        FunctionDefinitionAst myFunction;
        /// <summary>
        /// These binary operators will always return boolean value
        /// </summary>
        static TokenKind[] booleanBinaryOperators;

        /// <summary>
        /// These unary operator will return boolean value
        /// </summary>
        static TokenKind[] booleanUnaryOperators;

        static FindPipelineOutput()
        {
            booleanBinaryOperators = new TokenKind[] {
                TokenKind.Icontains,
                TokenKind.Inotcontains,
                TokenKind.Inotin,
                TokenKind.Iin,
                TokenKind.Is,
                TokenKind.IsNot,
                TokenKind.And,
                TokenKind.Or,
                TokenKind.Xor
            };

            booleanUnaryOperators = new TokenKind[] {
                TokenKind.Not,
                TokenKind.Exclaim
            };
        }

        /// <summary>
        /// Find the pipeline output
        /// </summary>
        /// <param name="ast"></param>

#if (PSV3||PSV4)

        public FindPipelineOutput(FunctionDefinitionAst ast)

#else

        public FindPipelineOutput(FunctionDefinitionAst ast, IEnumerable<TypeDefinitionAst> classes)

#endif
        {
            outputTypes = new List<Tuple<string, StatementAst>>();

#if !PSV3

            this.classes = classes;

#endif

            myFunction = ast;

            if (myFunction != null)
            {
                myFunction.Body.Visit(this);
            }
        }

        /// <summary>
        /// Get list of outputTypes from functiondefinitionast funcast
        /// </summary>
        /// <returns></returns>

#if (PSV3||PSV4)

        public static List<Tuple<string, StatementAst>> OutputTypes(FunctionDefinitionAst funcAst)
        {
            return (new FindPipelineOutput(funcAst)).outputTypes;
        }

#else
        public static List<Tuple<string, StatementAst>> OutputTypes(FunctionDefinitionAst funcAst, IEnumerable<TypeDefinitionAst> classes)
        {
            return (new FindPipelineOutput(funcAst, classes)).outputTypes;
        }

#endif

        /// <summary>
        /// Ignore assignment statement
        /// </summary>
        /// <param name="assignAst"></param>
        /// <returns></returns>
        public object VisitAssignmentStatement(AssignmentStatementAst assignAst)
        {
            return null;
        }

        /// <summary>
        /// Skip NamedAttributeArgumentAst
        /// </summary>
        /// <param name="namedAAAst"></param>
        /// <returns></returns>
        public object VisitNamedAttributeArgument(NamedAttributeArgumentAst namedAAAst)
        {
            return null;
        }

        /// <summary>
        /// Skip Error Expression Ast
        /// </summary>
        /// <param name="errorAst"></param>
        /// <returns></returns>
        public object VisitErrorExpression(ErrorExpressionAst errorAst)
        {
            return null;
        }

        /// <summary>
        /// Skip error statement ast
        /// </summary>
        /// <param name="errorStatementAst"></param>
        /// <returns></returns>
        public object VisitErrorStatement(ErrorStatementAst errorStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Skips function definition ast
        /// </summary>
        /// <param name="functionDefinitionAst"></param>
        /// <returns></returns>
        public object VisitFunctionDefinition(FunctionDefinitionAst functionDefinitionAst)
        {
            return null;
        }

        /// <summary>
        /// Skip ParameterAst
        /// </summary>
        /// <param name="parameterAst"></param>
        /// <returns></returns>
        public object VisitParameter(ParameterAst parameterAst)
        {
            return null;
        }

        /// <summary>
        /// Visit the pipeline of the paren ast
        /// </summary>
        /// <param name="parenAst"></param>
        /// <returns></returns>
        public object VisitParenExpression(ParenExpressionAst parenAst)
        {
            if (parenAst != null)
            {
                return parenAst.Pipeline.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Skips data statement
        /// </summary>
        /// <param name="dataStatementAst"></param>
        /// <returns></returns>
        public object VisitDataStatement(DataStatementAst dataStatementAst)
        {
            return null;
        }

        /// <summary>
        /// Visit scriptblockast
        /// </summary>
        /// <param name="scriptBlockAst"></param>
        /// <returns></returns>
        public object VisitScriptBlock(ScriptBlockAst scriptBlockAst)
        {
            if (scriptBlockAst != null)
            {
                if (scriptBlockAst.BeginBlock != null)
                {
                    scriptBlockAst.BeginBlock.Visit(this);
                }

                if (scriptBlockAst.ProcessBlock != null)
                {
                    scriptBlockAst.ProcessBlock.Visit(this);
                }

                if (scriptBlockAst.EndBlock != null)
                {
                    scriptBlockAst.EndBlock.Visit(this);
                }
            }

            return null;
        }

        /// <summary>
        /// Visit named block ast. Returns list of types outputted to the stream
        /// </summary>
        /// <param name="namedBlockAst"></param>
        /// <returns></returns>
        public object VisitNamedBlock(NamedBlockAst namedBlockAst)
        {
            if (namedBlockAst != null)
            {
                foreach (StatementAst block in namedBlockAst.Statements)
                {
                    object type = block.Visit(this);
                    if (type != null && type is string && !String.IsNullOrWhiteSpace(type as string))
                    {
                        outputTypes.Add(Tuple.Create(type as string, block));
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Visit statement block
        /// </summary>
        /// <param name="statementBlockAst"></param>
        /// <returns></returns>
        public object VisitStatementBlock(StatementBlockAst statementBlockAst)
        {
            if (statementBlockAst != null)
            {
                foreach (StatementAst block in statementBlockAst.Statements)
                {
                    object type = block.Visit(this);
                    if (type != null && type is string && !String.IsNullOrWhiteSpace(type as string))
                    {
                        outputTypes.Add(Tuple.Create(type as string, block));
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Only considers the case where there is one pipeline and it is command expression
        /// </summary>
        /// <param name="pipelineAst"></param>
        /// <returns></returns>
        public object VisitPipeline(PipelineAst pipelineAst)
        {
            // Handle the case with 1 pipeline element
            if (pipelineAst != null && pipelineAst.PipelineElements.Count == 1)
            {
                CommandExpressionAst cmAst = pipelineAst.PipelineElements[0] as CommandExpressionAst;

                if (cmAst != null)
                {
                    return cmAst.Visit(this);
                }
            }

            return null;
        }

        /// <summary>
        /// Visit body of trap
        /// </summary>
        /// <param name="trapAst"></param>
        /// <returns></returns>
        public object VisitTrap(TrapStatementAst trapAst)
        {
            if (trapAst != null)
            {
                return trapAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// In all the clauses, we skip the first item
        /// </summary>
        /// <param name="ifStatementAst"></param>
        /// <returns></returns>
        public object VisitIfStatement(IfStatementAst ifStatementAst)
        {
            if (ifStatementAst == null || ifStatementAst.Clauses == null || ifStatementAst.Clauses.Count == 0)
            {
                return null;
            }

            foreach (var clause in ifStatementAst.Clauses)
            {
                clause.Item2.Visit(this);
            }

            if (ifStatementAst.ElseClause != null)
            {
                ifStatementAst.ElseClause.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit SwitchStatement. Skip the condition
        /// </summary>
        /// <param name="switchStatementAst"></param>
        /// <returns></returns>
        public object VisitSwitchStatement(SwitchStatementAst switchStatementAst)
        {
            if (switchStatementAst == null || switchStatementAst.Clauses == null || switchStatementAst.Clauses.Count == 0)
            {
                return null;
            }

            foreach (var clause in switchStatementAst.Clauses)
            {
                // Skip item 1
                clause.Item2.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit foreach statement. Skip condition
        /// </summary>
        /// <param name="loopStatementAst"></param>
        /// <returns></returns>
        public object VisitForEachStatement(ForEachStatementAst foreachAst)
        {
            if (foreachAst != null)
            {
                foreachAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit Do While Statement. Skip Condition
        /// </summary>
        /// <param name="doWhileAst"></param>
        /// <returns></returns>
        public object VisitDoWhileStatement(DoWhileStatementAst doWhileAst)
        {
            if (doWhileAst != null)
            {
                doWhileAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit Do Until Statement. Skip Condition
        /// </summary>
        /// <param name="doWhileAst"></param>
        /// <returns></returns>
        public object VisitDoUntilStatement(DoUntilStatementAst doUntilAst)
        {
            if (doUntilAst != null)
            {
                doUntilAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit While Statement. Skip Condition
        /// </summary>
        /// <param name="doWhileAst"></param>
        /// <returns></returns>
        public object VisitWhileStatement(WhileStatementAst whileAst)
        {
            if (whileAst != null)
            {
                whileAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit forstatement. Skip Condition, Initializer and Iterator
        /// </summary>
        /// <param name="forAst"></param>
        /// <returns></returns>
        public object VisitForStatement(ForStatementAst forAst)
        {
            if (forAst != null)
            {
                forAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Skip command ast
        /// </summary>
        /// <param name="cmdAst"></param>
        /// <returns></returns>
        public object VisitCommand(CommandAst cmdAst)
        {
            return null;
        }

        /// <summary>
        /// Skip if type of convert is void
        /// </summary>
        /// <param name="convAst"></param>
        /// <returns></returns>
        public object VisitConvertExpression(ConvertExpressionAst convAst)
        {
            if (convAst != null)
            {
                if (convAst.Type.TypeName.GetReflectionType() != null)
                {
                    return convAst.Type.TypeName.GetReflectionType().FullName;
                }

                return convAst.Type.TypeName.FullName;
            }

            return null;
        }

        /// <summary>
        /// Skip fileRedirectionAst
        /// </summary>
        /// <param name="fileRedirectionAst"></param>
        /// <returns></returns>
        public object VisitFileRedirection(FileRedirectionAst fileRedirectionAst)
        {
            return null;
        }

        /// <summary>
        /// Visit script block expression
        /// </summary>
        /// <param name="scriptBlockAst"></param>
        /// <returns></returns>
        public object VisitScriptBlockExpression(ScriptBlockExpressionAst scriptBlockAst)
        {
            if (scriptBlockAst != null)
            {
                return scriptBlockAst.ScriptBlock.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Skip fileRedirectionAst
        /// </summary>
        /// <param name="fileRedirectionAst"></param>
        /// <returns></returns>
        public object VisitMergingRedirection(MergingRedirectionAst mergingAst)
        {
            return null;
        }

        /// <summary>
        /// Returns type of type constraint ast
        /// </summary>
        /// <param name="typeAst"></param>
        /// <returns></returns>
        public object VisitTypeConstraint(TypeConstraintAst typeAst)
        {
            if (typeAst != null)
            {
                if (typeAst.TypeName.GetReflectionType() != null)
                {
                    return typeAst.TypeName.GetReflectionType().FullName;
                }

                return typeAst.TypeName.FullName;
            }

            return null;
        }

        /// <summary>
        /// Skip throw statement.
        /// </summary>
        /// <param name="throwAst"></param>
        /// <returns></returns>
        public object VisitThrowStatement(ThrowStatementAst throwAst)
        {
            return null;
        }

        /// <summary>
        /// Returns type of typeExpressionAst
        /// </summary>
        /// <param name="typeExpressionAst"></param>
        /// <returns></returns>
        public object VisitTypeExpression(TypeExpressionAst typeExpressionAst)
        {
            if (typeExpressionAst != null)
            {
                if (typeExpressionAst.TypeName.GetReflectionType() != null)
                {
                    return typeExpressionAst.TypeName.GetReflectionType().FullName;
                }

                return typeExpressionAst.TypeName.FullName;
            }

            return null;
        }

        /// <summary>
        /// This is where we can get the type
        /// </summary>
        /// <param name="commandAst"></param>
        /// <returns></returns>
        public object VisitCommandExpression(CommandExpressionAst commandAst)
        {
            if (commandAst != null)
            {
                return commandAst.Expression.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Return the type of return statement
        /// </summary>
        /// <param name="returnStatementAst"></param>
        /// <returns></returns>
        public object VisitReturnStatement(ReturnStatementAst returnStatementAst)
        {
#if PSV3

            return Helper.Instance.GetTypeFromReturnStatementAst(myFunction, returnStatementAst);

#else

            return Helper.Instance.GetTypeFromReturnStatementAst(myFunction, returnStatementAst, classes);

#endif
        }

        /// <summary>
        /// Returns the type of memberexpressionast
        /// </summary>
        /// <param name="memAst"></param>
        /// <returns></returns>
        public object VisitMemberExpression(MemberExpressionAst memAst)
        {
#if PSV3

            return Helper.Instance.GetTypeFromMemberExpressionAst(memAst, myFunction);

#else

            return Helper.Instance.GetTypeFromMemberExpressionAst(memAst, myFunction, classes);

#endif
        }

        /// <summary>
        /// Returns the type of invoke member expression ast
        /// </summary>
        /// <param name="invokeAst"></param>
        /// <returns></returns>
        public object VisitInvokeMemberExpression(InvokeMemberExpressionAst invokeAst)
        {
#if PSV3

            return Helper.Instance.GetTypeFromMemberExpressionAst(invokeAst, myFunction);

#else

            return Helper.Instance.GetTypeFromMemberExpressionAst(invokeAst, myFunction, classes);

#endif
        }

        /// <summary>
        /// Visit a string constantexpressionast
        /// </summary>
        /// <param name="strAst"></param>
        /// <returns></returns>
        public object VisitStringConstantExpression(StringConstantExpressionAst strAst)
        {
            if (strAst != null)
            {
                return strAst.StaticType.FullName;
            }

            return null;
        }

        /// <summary>
        /// Skip command parameter
        /// </summary>
        /// <param name="cmdParamAst"></param>
        /// <returns></returns>
        public object VisitCommandParameter(CommandParameterAst cmdParamAst)
        {
            return null;
        }

        /// <summary>
        /// Visit a constantexpressionast
        /// </summary>
        /// <param name="constantExpressionAst"></param>
        /// <returns></returns>
        public object VisitConstantExpression(ConstantExpressionAst constantExpressionAst)
        {
            if (constantExpressionAst != null)
            {
                return constantExpressionAst.StaticType.FullName;
            }

            return null;
        }

        /// <summary>
        /// Skip break statement ast
        /// </summary>
        /// <param name="breakAst"></param>
        /// <returns></returns>
        public object VisitBreakStatement(BreakStatementAst breakAst)
        {
            return null;
        }

        /// <summary>
        /// Visit body, catch and finally clause
        /// </summary>
        /// <param name="tryAst"></param>
        /// <returns></returns>
        public object VisitTryStatement(TryStatementAst tryAst)
        {
            if (tryAst != null)
            {
                if (tryAst.Body != null)
                {
                    tryAst.Body.Visit(this);
                }

                if (tryAst.CatchClauses != null)
                {
                    foreach (var catchClause in tryAst.CatchClauses)
                    {
                        catchClause.Visit(this);
                    }
                }

                if (tryAst.Finally != null)
                {
                    tryAst.Finally.Visit(this);
                }
            }

            return null;
        }

        /// <summary>
        /// Visit body of catch clause
        /// </summary>
        /// <param name="catchAst"></param>
        /// <returns></returns>
        public object VisitCatchClause(CatchClauseAst catchAst)
        {
            if (catchAst != null)
            {
                return catchAst.Body.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Skip continue statement;
        /// </summary>
        /// <param name="contAst"></param>
        /// <returns></returns>
        public object VisitContinueStatement(ContinueStatementAst contAst)
        {
            return null;
        }

        public object VisitSubExpression(SubExpressionAst subExprAst)
        {
            if (subExprAst != null)
            {
                return subExprAst.SubExpression.Visit(this);
            }

            return null;
        }

        /// <summary>
        /// Visit the body of blockstatementast
        /// </summary>
        /// <param name="blockAst"></param>
        /// <returns></returns>
        public object VisitBlockStatement(BlockStatementAst blockAst)
        {
            return blockAst.Body.Visit(this);
        }

        /// <summary>
        /// Returns type of array
        /// </summary>
        /// <param name="arrayExprAst"></param>
        /// <returns></returns>
        public object VisitArrayExpression(ArrayExpressionAst arrayExprAst)
        {
            return typeof(System.Array).FullName;
        }

        /// <summary>
        /// Returns type of array
        /// </summary>
        /// <param name="arrayLiteral"></param>
        /// <returns></returns>
        public object VisitArrayLiteral(ArrayLiteralAst arrayLiteral)
        {
            return typeof(System.Array).FullName;
        }

        /// <summary>
        /// Returns type of hashtable
        /// </summary>
        /// <param name="hashtableAst"></param>
        /// <returns></returns>
        public object VisitHashtable(HashtableAst hashtableAst)
        {
            return typeof(System.Collections.Hashtable).FullName;
        }

        /// <summary>
        /// Returns type of variable
        /// </summary>
        /// <param name="varExpressionAst"></param>
        /// <returns></returns>
        public object VisitVariableExpression(VariableExpressionAst varExpressionAst)
        {
            return Helper.Instance.GetVariableTypeFromAnalysis(varExpressionAst, myFunction);
        }

        /// <summary>
        /// Return string type
        /// </summary>
        /// <param name="expandableStringAst"></param>
        /// <returns></returns>
        public object VisitExpandableStringExpression(ExpandableStringExpressionAst expandableStringAst)
        {
            return typeof(string).FullName;
        }

        /// <summary>
        /// Skip exit statement ast
        /// </summary>
        /// <param name="exitAst"></param>
        /// <returns></returns>
        public object VisitExitStatement(ExitStatementAst exitAst)
        {
            return null;
        }

        /// <summary>
        /// Visit attributedexpression
        /// </summary>
        /// <param name="attrExpr"></param>
        /// <returns></returns>
        public object VisitAttributedExpression(AttributedExpressionAst attrExpr)
        {
            return null;
        }

        /// <summary>
        /// Skip attribute ast
        /// </summary>
        /// <param name="attrAst"></param>
        /// <returns></returns>
        public object VisitAttribute(AttributeAst attrAst)
        {
            return null;
        }

        /// <summary>
        /// Skip param block
        /// </summary>
        /// <param name="paramBlockAst"></param>
        /// <returns></returns>
        public object VisitParamBlock(ParamBlockAst paramBlockAst)
        {
            return null;
        }

        /// <summary>
        /// Return type of the index expression
        /// </summary>
        /// <param name="indexAst"></param>
        /// <returns></returns>
        public object VisitIndexExpression(IndexExpressionAst indexAst)
        {
            if (indexAst != null && indexAst.Target is VariableExpressionAst)
            {
                Type type = Helper.Instance.GetTypeFromAnalysis(indexAst.Target as VariableExpressionAst, myFunction);
                if (type != null)
                {
                    Type elemType = type.GetElementType();
                    if (elemType != null)
                    {
                        return elemType.FullName;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Only returns boolean type for unary operator that returns boolean
        /// </summary>
        /// <param name="unaryAst"></param>
        /// <returns></returns>
        public object VisitUnaryExpression(UnaryExpressionAst unaryAst)
        {
            if (unaryAst != null && booleanUnaryOperators.Contains(unaryAst.TokenKind))
            {
                return typeof(bool).FullName;
            }

            return null;
        }

        /// <summary>
        /// Only returns boolean type for binary operator that returns boolean
        /// </summary>
        /// <param name="binAst"></param>
        /// <returns></returns>
        public object VisitBinaryExpression(BinaryExpressionAst binAst)
        {
            if (binAst != null && booleanBinaryOperators.Contains(binAst.Operator))
            {
                return typeof(bool).FullName;
            }

            return null;
        }

        /// <summary>
        /// Skips using expression ast
        /// </summary>
        /// <param name="usingExpressionAst"></param>
        /// <returns></returns>
        public object VisitUsingExpression(UsingExpressionAst usingExpressionAst)
        {
            return null;
        }
    }

    /// Class to represent a directed graph
    public class Digraph<T>
    {
        private List<List<int>> graph;
        private Dictionary<T, int> vertexIndexMap;

        /// <summary>
        /// Public constructor
        /// </summary>
        public Digraph()
        {
            graph = new List<List<int>>();
            vertexIndexMap = new Dictionary<T, int>();
        }

        /// <summary>
        /// Construct a directed graph that uses an EqualityComparer object for comparison with its vertices
        ///
        /// The class allows its client to use their choice of vertex type. To allow comparison for such a
        /// vertex type, client can pass their own EqualityComparer object
        /// </summary>
        /// <param name="equalityComparer"></param>
        public Digraph(IEqualityComparer<T> equalityComparer) : this()
        {
            if (equalityComparer == null)
            {
                throw new ArgumentNullException("equalityComparer");
            }

            vertexIndexMap = new Dictionary<T, int>(equalityComparer);
        }

        /// <summary>
        /// Return the number of vertices in the graph
        /// </summary>
        public int NumVertices
        {
            get { return graph.Count; }
        }

        /// <summary>
        /// Return an enumerator over the vertices in the graph
        /// </summary>
        public IEnumerable<T> GetVertices()
        {
            return vertexIndexMap.Keys;
        }

        /// <summary>
        /// Check if the given vertex is part of the graph.
        ///
        /// If the vertex is null, it will throw an ArgumentNullException.
        /// If the vertex is non-null but not present in the graph, it will throw an ArgumentOutOfRangeException
        /// </summary>
        /// <param name="vertex"></param>
        /// <returns>True if the graph contains the vertex, otherwise false</returns>
        public bool ContainsVertex(T vertex)
        {
            return vertexIndexMap.ContainsKey(vertex);
        }

        /// <summary>
        /// Get the neighbors of a given vertex
        ///
        /// If the vertex is null, it will throw an ArgumentNullException.
        /// If the vertex is non-null but not present in the graph, it will throw an ArgumentOutOfRangeException
        /// </summary>
        /// <param name="vertex"></param>
        /// <returns>An enumerator over the neighbors of the vertex</returns>
        public IEnumerable<T> GetNeighbors(T vertex)
        {
            ValidateVertexArgument(vertex);
            var idx = GetIndex(vertex);
            var idxVertexMap = vertexIndexMap.ToDictionary(x => x.Value, x => x.Key);
            foreach (var neighbor in graph[idx])
            {
                yield return idxVertexMap[neighbor];
            }
        }

        /// <summary>
        /// Gets the number of neighbors of the given vertex
        ///
        /// If the vertex is null, it will throw an ArgumentNullException.
        /// If the vertex is non-null but not present in the graph, it will throw an ArgumentOutOfRangeException
        /// </summary>
        /// <param name="vertex"></param>
        /// <returns></returns>
        public int GetOutDegree(T vertex)
        {
            ValidateVertexArgument(vertex);
            return graph[GetIndex(vertex)].Count;
        }

        /// <summary>
        /// Add a vertex to the graph
        ///
        /// If the vertex is null, it will throw an ArgumentNullException.
        /// If the vertex is non-null but already present in the graph, it will throw an ArgumentException
        /// </summary>
        /// <param name="vertex"></param>
        public void AddVertex(T vertex)
        {
            ValidateNotNull(vertex);
            if (GetIndex(vertex) != -1)
            {
                throw new ArgumentException(
                    String.Format(
                        Strings.DigraphVertexAlreadyExists,
                        vertex),
                    "vertex");
            }

            vertexIndexMap.Add(vertex, graph.Count);
            graph.Add(new List<int>());
        }

        /// <summary>
        /// Add an edge from one vertex to another
        ///
        /// If any input vertex is null, it will throw an ArgumentNullException
        /// If an edge is already present between the given vertices, it will throw an ArgumentException
        /// </summary>
        /// <param name="fromVertex"></param>
        /// <param name="toVertex"></param>
        public void AddEdge(T fromVertex, T toVertex)
        {
            ValidateVertexArgument(fromVertex);
            ValidateVertexArgument(toVertex);

            var toIdx = GetIndex(toVertex);
            var fromVertexList = graph[GetIndex(fromVertex)];
            if (fromVertexList.Contains(toIdx))
            {
                throw new ArgumentException(String.Format(
                    Strings.DigraphEdgeAlreadyExists,
                    fromVertex.ToString(),
                    toVertex.ToString()));
            }
            else
            {
                fromVertexList.Add(toIdx);
            }
        }

        /// <summary>
        /// Checks if a vertex is connected to another vertex within the graph
        /// </summary>
        /// <param name="vertex1"></param>
        /// <param name="vertex2"></param>
        /// <returns></returns>
        public bool IsConnected(T vertex1, T vertex2)
        {
            ValidateVertexArgument(vertex1);
            ValidateVertexArgument(vertex2);

            var visited = new bool[graph.Count];
            return IsConnected(GetIndex(vertex1), GetIndex(vertex2), ref visited);
        }

        /// <summary>
        /// Check if two vertices are connected
        /// </summary>
        /// <param name="fromIdx">Origin vertex</param>
        /// <param name="toIdx">Destination vertex</param>
        /// <param name="visited">A boolean array indicating whether a vertex has been visited or not</param>
        /// <returns>True if the vertices are conneted, otherwise false</returns>
        private bool IsConnected(int fromIdx, int toIdx, ref bool[] visited)
        {
            visited[fromIdx] = true;
            if (fromIdx == toIdx)
            {
                return true;
            }

            foreach(var vertexIdx in graph[fromIdx])
            {
                if (!visited[vertexIdx])
                {
                    if(IsConnected(vertexIdx, toIdx, ref visited))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Throw an ArgumentNullException if vertex is null
        /// </summary>
        private void ValidateNotNull(T vertex)
        {
            if (vertex == null)
            {
                throw new ArgumentNullException("vertex");
            }
        }

        /// <summary>
        /// Throw an ArgumentOutOfRangeException if vertex is not present in the graph
        /// </summary>
        private void ValidateVertexPresence(T vertex)
        {
            if (GetIndex(vertex) == -1)
            {
                throw new ArgumentOutOfRangeException(
                    String.Format(
                        Strings.DigraphVertexDoesNotExists,
                        vertex.ToString()),
                    "vertex");
            }
        }

        /// <summary>
        /// Throw exception if vertex is null or not present in graph
        /// </summary>
        private void ValidateVertexArgument(T vertex)
        {
            ValidateNotNull(vertex);
            ValidateVertexPresence(vertex);
        }

        /// <summary>
        /// Get the index of the vertex in the graph array
        /// </summary>
        private int GetIndex(T vertex)
        {
            int idx;
            return vertexIndexMap.TryGetValue(vertex, out idx) ? idx : -1;
        }
    }

    internal class PSVersionTable
    {
        private readonly string psVersionKey = "PSVersion";
        private readonly string psEditionKey = "PSEdition";
#if CORECLR
        public SemanticVersion PSVersion { get; private set; }
#else
        public Version PSVersion { get; private set; }
#endif
        public string PSEdition { get; private set; }

        public PSVersionTable(Hashtable psVersionTable)
        {
            if (psVersionTable == null)
            {
                throw new ArgumentNullException("psVersionTable");
            }

            if (!psVersionTable.ContainsKey(psVersionKey))
            {
                throw new ArgumentException("Input PSVersionTable does not contain PSVersion key"); // TODO localize
            }

#if CORECLR
            PSVersion = psVersionTable[psVersionKey] as SemanticVersion;
#else
            PSVersion = psVersionTable[psVersionKey] as Version;
#endif
            if (PSVersion == null)
            {
                throw new ArgumentException("Input PSVersionTable has invalid PSVersion value type"); // TODO localize
            }

            if (psVersionTable.ContainsKey(psEditionKey))
            {
                PSEdition = psVersionTable[psEditionKey] as string;
                if (PSEdition == null)
                {
                    throw new ArgumentException("Input PSVersionTable has invalid PSEdition value type"); // TODO localize
                }
            }
        }
    }
}

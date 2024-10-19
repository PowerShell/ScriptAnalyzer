// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management.Automation.Language;

namespace Microsoft.Windows.PowerShell.ScriptAnalyzer
{
    /// <summary>
    /// This class is used to analyze variable based on data flow
    /// </summary>
    public class VariableAnalysis : AnalysisBase
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="decorated"></param>
        public VariableAnalysis(IFlowGraph decorated) : base(decorated) { }

        private Dictionary<string, VariableAnalysisDetails> _variables;
        private readonly List<LoopGotoTargets> _loopTargets = new List<LoopGotoTargets>();
        private Dictionary<string, VariableAnalysisDetails> VariablesDictionary;
        private Dictionary<string, VariableAnalysisDetails> InternalVariablesDictionary;

        /// <summary>
        /// Return parameters of a functionmemberast or functiondefinitionast
        /// </summary>
        /// <param name="ast"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        private IEnumerable<ParameterAst> FindParameters(Ast ast, Type type)
        {
            IEnumerable<Ast> parameters = ast.FindAll(item => item is ParameterAst, true);

            foreach (ParameterAst parameter in parameters)
            {
                Ast parent = parameter.Parent;
                while (parent != null)
                {
                    if (parent.GetType() == type)
                    {
                        if (parent != ast)
                        {
                            break;
                        }

                        yield return parameter;
                    }

                    parent = parent.Parent;
                }
            }
        }

        private void ProcessParameters(IEnumerable<ParameterAst> parameters)
        {
            foreach (var parameter in parameters)
            {
                var variablePath = parameter.Name.VariablePath;
                bool isSwitchOrMandatory = false;
                Type type = null;
                foreach (var paramAst in parameter.Attributes)
                {
                    if (paramAst is TypeConstraintAst)
                    {
                        if (type == null)
                        {
                            type = paramAst.TypeName.GetReflectionType();
                        }

                        if (paramAst.TypeName.GetReflectionType() == typeof(System.Management.Automation.SwitchParameter))
                        {
                            isSwitchOrMandatory = true;
                        }
                    }
                    else if (paramAst is AttributeAst)
                    {
                        var args = (paramAst as AttributeAst).NamedArguments;
                        if (args != null)
                        {
                            foreach (NamedAttributeArgumentAst arg in args)
                            {
                                if (String.Equals(arg.ArgumentName, "mandatory", StringComparison.OrdinalIgnoreCase))
                               {
                                    // check for the case mandatory=$true and just mandatory
                                    if (arg.ExpressionOmitted || (!arg.ExpressionOmitted && String.Equals(arg.Argument.Extent.Text, "$true", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        isSwitchOrMandatory = true;
                                    }
                                }
                            }
                        }
                    }
                }

                var varName = AssignmentTarget.GetUnaliasedVariableName(variablePath);
                var details = _variables[varName];
                details.Type = type ?? details.Type ?? typeof(object);

                if (parameter.DefaultValue != null)
                {
                    var assignTarget = new AssignmentTarget(varName, type);

                    if (parameter.DefaultValue is ConstantExpressionAst)
                    {
                        assignTarget.Constant = (parameter.DefaultValue as ConstantExpressionAst).Value;
                        assignTarget.Type = assignTarget.Constant == null ? typeof(object) : assignTarget.Constant.GetType();
                    }

                    Entry.AddAst(assignTarget);
                }
                else if (isSwitchOrMandatory)
                {
                    // Consider switch or mandatory parameter as already initialized
                    Entry.AddAst(new AssignmentTarget(varName, type));
                }
                else
                {
                    VariableTarget varTarget = new VariableTarget(parameter.Name);
                    varTarget.Type = details.Type;
                    Entry.AddAst(varTarget);
                }
            }
        }

        /// <summary>
        /// Used to analyze scriptblock, functionmemberast or functiondefinitionast
        /// </summary>
        /// <param name="ast"></param>
        /// <returns></returns>
        public void AnalyzeImpl(Ast ast, VariableAnalysis outerAnalysis)
        {

            #if PSV3

            if (!(ast is ScriptBlockAst || ast is FunctionDefinitionAst))
            
            #else            

            if (!(ast is ScriptBlockAst || ast is FunctionMemberAst || ast is FunctionDefinitionAst))

            #endif
            {
                return;
            }

            _variables = FindAllVariablesVisitor.Visit(ast);

            Init();

            #if PSV3

            if (ast is FunctionDefinitionAst)

            #else

            if (ast is FunctionMemberAst || ast is FunctionDefinitionAst)

            #endif
            {
                IEnumerable<ParameterAst> parameters = FindParameters(ast, ast.GetType());
                if (parameters != null)
                {
                    ProcessParameters(parameters);
                }
            }
            else
            {
                ScriptBlockAst sbAst = ast as ScriptBlockAst;
                if (sbAst != null && sbAst.ParamBlock != null && sbAst.ParamBlock.Parameters != null)
                {
                    ProcessParameters(sbAst.ParamBlock.Parameters);
                }
            }

            #if PSV3

            if (ast is FunctionDefinitionAst)

            #else

            if (ast is FunctionMemberAst)
            {
                (ast as FunctionMemberAst).Body.Visit(this.Decorator);
            }
            else if (ast is FunctionDefinitionAst)
            
            #endif

            {
                (ast as FunctionDefinitionAst).Body.Visit(this.Decorator);
            }
            else
            {
                ast.Visit(this.Decorator);
            }

            Ast parent = ast;

            while (parent.Parent != null)
            {
                parent = parent.Parent;
            }

            #if !(PSV3||PSV4)

            List<TypeDefinitionAst> classes = parent.FindAll(item =>
                item is TypeDefinitionAst && (item as TypeDefinitionAst).IsClass, true)
                .Cast<TypeDefinitionAst>().ToList();

            #endif

            if (outerAnalysis != null)
            {
                // Initialize the variables from outside
                var outerDictionary = outerAnalysis.InternalVariablesDictionary;
                foreach (var details in outerDictionary.Values)
                {
                    if (details.DefinedBlock != null)
                    {
                        var assignTarget = new AssignmentTarget(details.RealName, details.Type);
                        assignTarget.Constant = details.Constant;
                        if (!_variables.ContainsKey(assignTarget.Name))
                        {
                            _variables.Add(assignTarget.Name, new VariableAnalysisDetails
                            {
                                Name = assignTarget.Name,
                                RealName = assignTarget.Name,
                                Type = assignTarget.Type
                            });
                        }
                        Entry.AddFirstAst(assignTarget);
                    }
                }

                foreach (var key in _variables.Keys)
                {
                    if (outerDictionary.ContainsKey(key))
                    {
                        var outerItem = outerDictionary[key];
                        var innerItem = _variables[key];
                        innerItem.Constant = outerItem.Constant;
                        innerItem.Name = outerItem.Name;
                        innerItem.RealName = outerItem.RealName;
                        innerItem.Type = outerItem.Type;
                    }
                }
            }

            #if PSV3

            var dictionaries = Block.SparseSimpleConstants(_variables, Entry);

            #else

            var dictionaries = Block.SparseSimpleConstants(_variables, Entry, classes);

            #endif
            VariablesDictionary = dictionaries.Item1;
            InternalVariablesDictionary = new Dictionary<string, VariableAnalysisDetails>(StringComparer.OrdinalIgnoreCase);

            foreach (var KVP in dictionaries.Item2)
            {
                var analysis = KVP.Value;
                if (analysis == null)
                {
                    continue;
                }

                if (!InternalVariablesDictionary.ContainsKey(analysis.RealName))
                {
                    InternalVariablesDictionary.Add(analysis.RealName, analysis);
                }
                else
                {
                    InternalVariablesDictionary[analysis.RealName] = analysis;
                }
            }
        }

        /// <summary>
        /// Updates the variablesdictionary of the outeranalysis based on that of the inneranalysis
        /// </summary>
        /// <param name="OuterAnalysis"></param>
        /// <param name="InnerAnalysis"></param>
        internal static void UpdateOuterAnalysis(VariableAnalysis OuterAnalysis, VariableAnalysis InnerAnalysis)
        {
            if (OuterAnalysis == null || InnerAnalysis == null)
            {
                return;
            }

            foreach (var key in InnerAnalysis.VariablesDictionary.Keys)
            {
                if (OuterAnalysis.VariablesDictionary.ContainsKey(key))
                {
                    OuterAnalysis.VariablesDictionary[key] = InnerAnalysis.VariablesDictionary[key];
                }
                else
                {
                    OuterAnalysis.VariablesDictionary.Add(key, InnerAnalysis.VariablesDictionary[key]);
                }
            }
        }

        /// <summary>
        /// Return variableanalysisdetails for VarTarget.
        /// This function should only be called after Block.SparseSimpleConstants are called.
        /// </summary>
        /// <param name="varTarget"></param>
        /// <returns></returns>
        public VariableAnalysisDetails GetVariableAnalysis(VariableExpressionAst varTarget)
        {
            if (varTarget == null)
            {
                return null;
            }

            string key = AnalysisDictionaryKey(varTarget);

            if (!VariablesDictionary.ContainsKey(key))
            {
                return null;
            }

            return VariablesDictionary[key];
        }

        internal static string AnalysisDictionaryKey(VariableExpressionAst varExprAst)
        {
            if (varExprAst == null)
            {
                return String.Empty;
            }

            return String.Format(CultureInfo.CurrentCulture,
                "{0}s{1}e{2}",
                AssignmentTarget.GetUnaliasedVariableName(varExprAst.VariablePath),
                varExprAst.Extent.StartOffset,
                varExprAst.Extent.EndOffset
                );
        }

        /// <summary>
        /// Returns true if the variable is initialized.
        /// This function should only be called after SparseSimpleConstants are called.
        /// </summary>
        /// <param name="varTarget"></param>
        /// <returns></returns>
        public bool IsUninitialized(VariableExpressionAst varTarget)
        {
            if (varTarget == null)
            {
                return false;
            }

            var analysis = GetVariableAnalysis(varTarget);

            if (analysis == null)
            {
                return false;
            }

            return analysis.DefinedBlock == null
                && !(SpecialVars.InitializedVariables.Contains(analysis.Name, StringComparer.OrdinalIgnoreCase) || 
                SpecialVars.InitializedVariables.Contains(analysis.RealName, StringComparer.OrdinalIgnoreCase)) 
                && !IsGlobalOrEnvironment(varTarget);
        }

        /// <summary>
        /// Returns true if the variable is not a global variable nor environment variable
        /// </summary>
        /// <param name="varTarget"></param>
        /// <returns></returns>
        public static bool IsGlobalOrEnvironment(VariableExpressionAst varTarget)
        {
            if (varTarget != null)
            {
                return (varTarget.VariablePath.IsGlobal
                        || String.Equals(varTarget.VariablePath.DriveName, "env", StringComparison.OrdinalIgnoreCase));
            }

            return false;

        }

        /// <summary>
        /// Get assignment targets from expressionast
        /// </summary>
        /// <param name="expressionAst"></param>
        /// <returns></returns>
        internal static IEnumerable<ExpressionAst> GetAssignmentTargets(ExpressionAst expressionAst)
        {
            var parenExpr = expressionAst as ParenExpressionAst;
            if (parenExpr != null)
            {
                foreach (var e in GetAssignmentTargets(parenExpr.Pipeline.GetPureExpression()))
                {
                    yield return e;
                }
            }
            else
            {
                var arrayLiteral = expressionAst as ArrayLiteralAst;
                if (arrayLiteral != null)
                {
                    foreach (var e in arrayLiteral.Elements.SelectMany(GetAssignmentTargets))
                    {
                        yield return e;
                    }
                }
                else
                {
                    yield return expressionAst;
                }
            }
        }

        /// <summary>
        /// Visit assignment statement
        /// </summary>
        /// <param name="assignmentStatementAst"></param>
        /// <returns></returns>
        public override object VisitAssignmentStatement(AssignmentStatementAst assignmentStatementAst)
        {
            if (assignmentStatementAst == null)
            {
                return null;
            }

            base.VisitAssignmentStatement(assignmentStatementAst);

            foreach (var assignTarget in GetAssignmentTargets(assignmentStatementAst.Left))
            {
                var leftAst = assignTarget;
                while (leftAst is AttributedExpressionAst)
                {
                    leftAst = ((AttributedExpressionAst)leftAst).Child;
                }

                if (leftAst is VariableExpressionAst)
                {
                    var varPath = ((VariableExpressionAst)leftAst).VariablePath;

                    if (_variables.ContainsKey(AssignmentTarget.GetUnaliasedVariableName(varPath)))
                    {
                        var details = _variables[AssignmentTarget.GetUnaliasedVariableName(varPath)];
                        details.AssignedBlocks.Add(Current);
                    }

                    Current.AddAst(new AssignmentTarget(assignmentStatementAst));
                }
                else
                {
                    // We skip things like $a.test = 3. In this case we will just test
                    // for variable $a
                    assignTarget.Visit(this.Decorator);
                }
            }

            return null;
        }

        /// <summary>
        /// Visit variable expression ast
        /// </summary>
        /// <param name="variableExpressionAst"></param>
        /// <returns></returns>
        public override object VisitVariableExpression(VariableExpressionAst variableExpressionAst)
        {
            if (variableExpressionAst == null) return null;

            var varPath = variableExpressionAst.VariablePath;

            if (_variables.ContainsKey(AssignmentTarget.GetUnaliasedVariableName(varPath)))
            {
                var details = _variables[AssignmentTarget.GetUnaliasedVariableName(varPath)];
                Current.AddAst(new VariableTarget(variableExpressionAst));
                details.AssociatedAsts.Add(variableExpressionAst);
            }

            return base.VisitVariableExpression(variableExpressionAst);
        }
    }
}

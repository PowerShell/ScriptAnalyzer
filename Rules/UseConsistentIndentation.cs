// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
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
    /// UseConsistentIndentation: Checks if indentation is consistent throughout the source file.
    /// </summary>
#if !CORECLR
    [Export(typeof(IScriptRule))]
#endif
    public class UseConsistentIndentation : ConfigurableRule
    {
        /// <summary>
        /// The indentation size in number of space characters.
        ///
        /// Default value if 4.
        /// </summary>
        [ConfigurableRuleProperty(defaultValue: 4)]
        public int IndentationSize { get; protected set; }


        // Cannot name to IndentationKind due to the enum type of the same name.
        /// <summary>
        /// Represents the kind of indentation to be used.
        ///
        /// Possible values are: `space`, `tab`. If any invalid value is given, the
        /// property defaults to `space`.
        ///
        /// `space` means `IndentationSize` number of `space` characters are used to provide one level of indentation.
        /// `tab` means a tab character, `\t`.
        ///</summary>
        [ConfigurableRuleProperty(defaultValue: "space")]
        public string Kind
        {
            get
            {
                return indentationKind.ToString();
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value) ||
                    !Enum.TryParse<IndentationKind>(value, true, out indentationKind))
                {
                    indentationKind = IndentationKind.Space;
                }
            }
        }


        [ConfigurableRuleProperty(defaultValue: "IncreaseIndentationForFirstPipeline")]
        public string PipelineIndentation
        {
            get
            {
                return pipelineIndentationStyle.ToString();
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value) ||
                    !Enum.TryParse(value, true, out pipelineIndentationStyle))
                {
                    pipelineIndentationStyle = PipelineIndentationStyle.IncreaseIndentationForFirstPipeline;
                }
            }
        }

        private bool insertSpaces;
        private char indentationChar;
        private int indentationLevelMultiplier;

        // TODO Enable auto when the rule is able to detect indentation
        private enum IndentationKind {
            Space,
            Tab,
            // Auto
        };

        private enum PipelineIndentationStyle
        {
            IncreaseIndentationForFirstPipeline,
            IncreaseIndentationAfterEveryPipeline,
            NoIndentation,
            None
        }

        // TODO make this configurable
        private IndentationKind indentationKind = IndentationKind.Space;

        private PipelineIndentationStyle pipelineIndentationStyle = PipelineIndentationStyle.IncreaseIndentationForFirstPipeline;

        /// <summary>
        /// Analyzes the given ast to find violations.
        /// </summary>
        /// <param name="ast">AST to be analyzed. This should be non-null</param>
        /// <param name="fileName">Name of file that corresponds to the input AST.</param>
        /// <returns>A an enumerable type containing the violations</returns>
        public override IEnumerable<DiagnosticRecord> AnalyzeScript(Ast ast, string fileName)
        {
            if (ast == null)
            {
                throw new ArgumentNullException("ast");
            }

            // we add this switch because there is no clean way
            // to disable the rule by default
            if (!Enable)
            {
                return Enumerable.Empty<DiagnosticRecord>();
            }

            // It is more efficient to initialize these fields in ConfigurRule method
            // but when the rule will enable `Auto` IndentationKind, we will anyways need to move
            // the setting of these variables back here after the rule detects the indentation kind for
            // each invocation.
            insertSpaces = indentationKind == IndentationKind.Space;
            indentationChar = insertSpaces ? ' ' : '\t';
            indentationLevelMultiplier = insertSpaces ? IndentationSize : 1;

            var tokens = Helper.Instance.Tokens;
            var diagnosticRecords = new List<DiagnosticRecord>();
            var indentationLevel = 0;
            var currentIndenationLevelIncreaseDueToPipelines = 0;
            var onNewLine = true;
            var pipelineAsts = ast.FindAll(testAst => testAst is PipelineAst && (testAst as PipelineAst).PipelineElements.Count > 1, true).ToList();
            /*
                When an LParen and LBrace are on the same line, it can lead to too much de-indentation.
                In order to prevent the RParen code from de-indenting too much, we keep a stack of when we skipped the indentation
                caused by tokens that require a closing RParen (which are LParen, AtParen and DollarParen).
            */
            var lParenSkippedIndentation = new Stack<bool>();
            
            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                var token = tokens[tokenIndex];

                if (token.Kind == TokenKind.EndOfInput)
                {
                    break;
                }

                switch (token.Kind)
                {
                    case TokenKind.AtCurly:
                    case TokenKind.LCurly:
                        AddViolation(token, indentationLevel++, diagnosticRecords, ref onNewLine);
                        break;

                    case TokenKind.DollarParen:
                    case TokenKind.AtParen:
                        lParenSkippedIndentation.Push(false);
                        AddViolation(token, indentationLevel++, diagnosticRecords, ref onNewLine);
                        break;

                    case TokenKind.LParen:
                        AddViolation(token, indentationLevel, diagnosticRecords, ref onNewLine);
                        // When a line starts with a parenthesis and it is not the last non-comment token of that line,
                        // then indentation does not need to be increased.
                        if ((tokenIndex == 0 || tokens[tokenIndex - 1].Kind == TokenKind.NewLine) &&
                            NextTokenIgnoringComments(tokens, tokenIndex)?.Kind != TokenKind.NewLine)
                        {
                            onNewLine = false;
                            lParenSkippedIndentation.Push(true);
                            break;
                        }
                        lParenSkippedIndentation.Push(false);
                        indentationLevel++;
                        break;

                    case TokenKind.Pipe:
                        if (pipelineIndentationStyle == PipelineIndentationStyle.None) { break; }
                        bool pipelineIsFollowedByNewlineOrLineContinuation =
                            PipelineIsFollowedByNewlineOrLineContinuation(tokens, tokenIndex);
                        if (!pipelineIsFollowedByNewlineOrLineContinuation)
                        {
                            break;
                        }
                        if (pipelineIndentationStyle == PipelineIndentationStyle.IncreaseIndentationAfterEveryPipeline)
                        {
                            AddViolation(token, indentationLevel++, diagnosticRecords, ref onNewLine);
                            currentIndenationLevelIncreaseDueToPipelines++;
                            break;
                        }
                        if (pipelineIndentationStyle == PipelineIndentationStyle.IncreaseIndentationForFirstPipeline)
                        {
                            bool isFirstPipeInPipeline = pipelineAsts.Any(pipelineAst =>
                                PositionIsEqual(LastPipeOnFirstLineWithPipeUsage((PipelineAst)pipelineAst).Extent.EndScriptPosition,
                                   tokens[tokenIndex - 1].Extent.EndScriptPosition));
                            if (isFirstPipeInPipeline)
                            {
                                AddViolation(token, indentationLevel++, diagnosticRecords, ref onNewLine);
                                currentIndenationLevelIncreaseDueToPipelines++;
                            }
                        }
                        break;

                    case TokenKind.RParen:
                        bool matchingLParenIncreasedIndentation = false;
                        if (lParenSkippedIndentation.Count > 0)
                        {
                            matchingLParenIncreasedIndentation = lParenSkippedIndentation.Pop();
                        }
                        if (matchingLParenIncreasedIndentation)
                        {
                            onNewLine = false;
                            break;
                        }
                        indentationLevel = ClipNegative(indentationLevel - 1);
                        AddViolation(token, indentationLevel, diagnosticRecords, ref onNewLine);
                        break;

                    case TokenKind.RCurly:
                        indentationLevel = ClipNegative(indentationLevel - 1);
                        AddViolation(token, indentationLevel, diagnosticRecords, ref onNewLine);
                        break;

                    case TokenKind.NewLine:
                    case TokenKind.LineContinuation:
                        onNewLine = true;
                        break;

                    default:
                        // we do not want to make a call for every token, hence
                        // we add this redundant check
                        if (onNewLine)
                        {
                            var tempIndentationLevel = indentationLevel;

                            // Check if the preceding character is an escape character
                            if (tokenIndex > 0 && tokens[tokenIndex - 1].Kind == TokenKind.LineContinuation)
                            {
                                ++tempIndentationLevel;
                            }

                            // check for comments in between multi-line commands with line continuation
                            if (tokenIndex > 2 && tokens[tokenIndex - 1].Kind == TokenKind.NewLine
                                && tokens[tokenIndex - 2].Kind == TokenKind.Comment)
                            {
                                int searchForPrecedingLineContinuationIndex = tokenIndex - 2;
                                while (searchForPrecedingLineContinuationIndex  > 0 && tokens[searchForPrecedingLineContinuationIndex].Kind == TokenKind.Comment)
                                {
                                    searchForPrecedingLineContinuationIndex--;
                                }

                                if (searchForPrecedingLineContinuationIndex >= 0 && tokens[searchForPrecedingLineContinuationIndex].Kind == TokenKind.LineContinuation)
                                {
                                    tempIndentationLevel++;
                                }
                            }

                            if (pipelineIndentationStyle == PipelineIndentationStyle.None && PreviousLineEndedWithPipe(tokens, tokenIndex, token))
                            {
                                onNewLine = false;
                                continue;
                            }

                            bool lineHasPipelineBeforeToken = LineHasPipelineBeforeToken(tokens, tokenIndex, token);
                            AddViolation(token, tempIndentationLevel, diagnosticRecords, ref onNewLine, lineHasPipelineBeforeToken);
                        }
                        break;
                }

                if (pipelineIndentationStyle == PipelineIndentationStyle.None) { continue; }

                // Check if the current token matches the end of a PipelineAst
                PipelineAst matchingPipeLineAstEnd = MatchingPipelineAstEnd(pipelineAsts, token);
                if (matchingPipeLineAstEnd == null)
                {
                    continue;
                }
                IScriptExtent firstPipelineElementExtent = matchingPipeLineAstEnd.PipelineElements[0].Extent;
                IScriptExtent lastPipelineElementExtent = matchingPipeLineAstEnd.PipelineElements[matchingPipeLineAstEnd.PipelineElements.Count - 1].Extent;
                bool pipelinesSpanOnlyOneLine = firstPipelineElementExtent.EndLineNumber == lastPipelineElementExtent.EndLineNumber
                                             || firstPipelineElementExtent.StartLineNumber == lastPipelineElementExtent.StartLineNumber;
                if (pipelinesSpanOnlyOneLine)
                {
                    continue;
                }

                if (pipelineIndentationStyle == PipelineIndentationStyle.IncreaseIndentationForFirstPipeline ||
                    pipelineIndentationStyle == PipelineIndentationStyle.IncreaseIndentationAfterEveryPipeline)
                {
                    indentationLevel = ClipNegative(indentationLevel - currentIndenationLevelIncreaseDueToPipelines);
                    currentIndenationLevelIncreaseDueToPipelines = 0;
                }
            }

            return diagnosticRecords;
        }

        private static Token NextTokenIgnoringComments(Token[] tokens, int startIndex)
        {
            if (startIndex >= tokens.Length - 1)
            {
                return null;
            }
            
            for (int i = startIndex + 1; i < tokens.Length; i++)
            {
                switch (tokens[i].Kind)
                {
                    case TokenKind.Comment:
                        continue;

                    default:
                        return tokens[i];
                }
            }
            
            // We've run out of tokens
            return null;
        }
        
        private static bool PipelineIsFollowedByNewlineOrLineContinuation(Token[] tokens, int startIndex)
        {
            if (startIndex >= tokens.Length - 1)
            {
                return false;
            }
            
            Token nextToken = null;
            for (int i = startIndex + 1; i < tokens.Length; i++)
            {
                nextToken = tokens[i];

                switch (nextToken.Kind)
                {
                    case TokenKind.Comment:
                        continue;

                    case TokenKind.NewLine:
                    case TokenKind.LineContinuation:
                        return true;

                    default:
                        return false;
                }
            }
            
            // We've run out of tokens but haven't seen a newline
            return false;
        }

        private static bool PreviousLineEndedWithPipe(Token[] tokens, int tokenIndex, Token token)
        {
            if (tokenIndex < 2 || token.Extent.StartLineNumber == 1)
            {
                return false;
            }

            int searchIndex = tokenIndex - 2;
            int searchLine;
            do
            {
                searchLine = tokens[searchIndex].Extent.StartLineNumber;
                if (tokens[searchIndex].Kind == TokenKind.Comment)
                {
                    searchIndex--;
                }
                else if (tokens[searchIndex].Kind == TokenKind.Pipe)
                {
                    return true;
                }
                else
                {
                    break;
                }
            } while (searchLine == token.Extent.StartLineNumber - 1 && searchIndex >= 0);

            return false;
        }

        private static bool LineHasPipelineBeforeToken(Token[] tokens, int tokenIndex, Token token)
        {
            int searchIndex = tokenIndex;
            int searchLine = token.Extent.StartLineNumber;
            do
            {
                searchLine = tokens[searchIndex].Extent.StartLineNumber;
                int searchcolumn = tokens[searchIndex].Extent.StartColumnNumber;
                if (tokens[searchIndex].Kind == TokenKind.Pipe && searchcolumn < token.Extent.StartColumnNumber)
                {
                    return true;
                }
                searchIndex--;
            } while (searchLine == token.Extent.StartLineNumber && searchIndex >= 0);
            return false;
        }

        private static CommandBaseAst LastPipeOnFirstLineWithPipeUsage(PipelineAst pipelineAst)
        {
            CommandBaseAst lastPipeOnFirstLineWithPipeUsage = pipelineAst.PipelineElements[0];
            foreach (CommandBaseAst pipelineElement in pipelineAst.PipelineElements.Skip(1))
            {
                if (pipelineElement.Extent.StartLineNumber == pipelineAst.PipelineElements[0].Extent.StartLineNumber ||
                    pipelineElement.Extent.StartLineNumber == pipelineAst.PipelineElements[0].Extent.EndLineNumber ||
                    pipelineElement.Extent.EndLineNumber == pipelineAst.PipelineElements[0].Extent.EndLineNumber)
                {
                    lastPipeOnFirstLineWithPipeUsage = pipelineElement;
                }
            }
            return lastPipeOnFirstLineWithPipeUsage;
        }

        private static PipelineAst MatchingPipelineAstEnd(List<Ast> pipelineAsts, Token token)
        {
            PipelineAst matchingPipeLineAstEnd = null;
            for (int i = 0; i < pipelineAsts.Count; i++)
            {
                if (pipelineAsts[i].Extent.EndScriptPosition.LineNumber > token.Extent.EndScriptPosition.LineNumber)
                {
                    break;
                }

                if (PositionIsEqual(pipelineAsts[i].Extent.EndScriptPosition, token.Extent.EndScriptPosition))
                {
                    matchingPipeLineAstEnd = pipelineAsts[i] as PipelineAst;
                    break;
                }
            }

            return matchingPipeLineAstEnd;
        }

        private static bool PositionIsEqual(IScriptPosition position1, IScriptPosition position2)
        {
            return position1.ColumnNumber == position2.ColumnNumber &&
                   position1.LineNumber == position2.LineNumber &&
                   position1.File == position2.File;
        }

        /// <summary>
        /// Retrieves the common name of this rule.
        /// </summary>
        public override string GetCommonName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseConsistentIndentationCommonName);
        }

        /// <summary>
        /// Retrieves the description of this rule.
        /// </summary>
        public override string GetDescription()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.UseConsistentIndentationDescription);
        }

        /// <summary>
        /// Retrieves the name of this rule.
        /// </summary>
        public override string GetName()
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.NameSpaceFormat,
                GetSourceName(),
                Strings.UseConsistentIndentationName);
        }

        /// <summary>
        /// Retrieves the severity of the rule: error, warning or information.
        /// </summary>
        public override RuleSeverity GetSeverity()
        {
            return RuleSeverity.Warning;
        }

        /// <summary>
        /// Gets the severity of the returned diagnostic record: error, warning, or information.
        /// </summary>
        /// <returns></returns>
        public DiagnosticSeverity GetDiagnosticSeverity()
        {
            return DiagnosticSeverity.Warning;
        }

        /// <summary>
        /// Retrieves the name of the module/assembly the rule is from.
        /// </summary>
        public override string GetSourceName()
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.SourceName);
        }

        /// <summary>
        /// Retrieves the type of the rule, Builtin, Managed or Module.
        /// </summary>
        public override SourceType GetSourceType()
        {
            return SourceType.Builtin;
        }

        private void AddViolation(
            Token token,
            int expectedIndentationLevel,
            List<DiagnosticRecord> diagnosticRecords,
            ref bool onNewLine,
            bool lineHasPipelineBeforeToken = false)
        {
            if (onNewLine)
            {
                onNewLine = false;
                if (token.Extent.StartColumnNumber - 1 != GetIndentation(expectedIndentationLevel))
                {
                    var fileName = token.Extent.File;
                    var extent = token.Extent;
                    var violationExtent = extent = new ScriptExtent(
                        new ScriptPosition(
                            fileName,
                            extent.StartLineNumber,
                            1, // first column in the line
                            extent.StartScriptPosition.Line),
                        new ScriptPosition(
                            fileName,
                            extent.StartLineNumber,
                            extent.StartColumnNumber,
                            extent.StartScriptPosition.Line));
                    diagnosticRecords.Add(
                        new DiagnosticRecord(
                            String.Format(CultureInfo.CurrentCulture, Strings.UseConsistentIndentationError),
                            violationExtent,
                            GetName(),
                            GetDiagnosticSeverity(),
                            fileName,
                            null,
                            GetSuggestedCorrections(token, expectedIndentationLevel, lineHasPipelineBeforeToken)));
                }
            }
        }

        private List<CorrectionExtent> GetSuggestedCorrections(
            Token token,
            int indentationLevel,
            bool lineHasPipelineBeforeToken = false)
        {
            // TODO Add another constructor for correction extent that takes extent
            // TODO handle param block
            // TODO handle multiline commands

            var corrections = new List<CorrectionExtent>();
            var optionalPipeline = lineHasPipelineBeforeToken ? "| " : string.Empty;
            corrections.Add(new CorrectionExtent(
                token.Extent.StartLineNumber,
                token.Extent.EndLineNumber,
                1,
                token.Extent.EndColumnNumber,
                GetIndentationString(indentationLevel) + optionalPipeline + token.Extent.Text,
                token.Extent.File));
            return corrections;
        }

        private static int ClipNegative(int x)
        {
            return x > 0 ? x : 0;
        }

        private int GetIndentation(int indentationLevel)
        {
            // todo if condition can be evaluated during rule configuration
            return indentationLevel * indentationLevelMultiplier;
        }

        private string GetIndentationString(int indentationLevel)
        {
            return new string(indentationChar, GetIndentation(indentationLevel));
        }
    }
}

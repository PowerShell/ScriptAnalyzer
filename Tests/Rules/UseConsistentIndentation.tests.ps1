# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $testRootDirectory = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $testRootDirectory "PSScriptAnalyzerTestHelper.psm1")
}

Describe "UseConsistentIndentation" {
    BeforeAll {
        function Invoke-FormatterAssertion {
            param(
                [string] $ScriptDefinition,
                [string] $ExpectedScriptDefinition,
                [int] $NumberOfExpectedWarnings,
                [hashtable] $Settings
            )

            # Unit test just using this rule only
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $scriptDefinition -Settings $settings
            $violations.Count | Should -Be $NumberOfExpectedWarnings -Because $ScriptDefinition
            Invoke-Formatter -ScriptDefinition $scriptDefinition -Settings $settings | Should -Be $ExpectedScriptDefinition -Because $ScriptDefinition
            # Integration test with all default formatting rules
            Invoke-Formatter -ScriptDefinition $scriptDefinition | Should -Be $ExpectedScriptDefinition -Because $ScriptDefinition
        }
    }
    BeforeEach {
        $indentationUnit = ' '
        $indentationSize = 4
        $ruleConfiguration = @{
            Enable          = $true
            IndentationSize = 4
            PipelineIndentation = 'IncreaseIndentationForFirstPipeline'
            Kind            = 'space'
        }

        $settings = @{
            IncludeRules = @("PSUseConsistentIndentation")
            Rules        = @{
                PSUseConsistentIndentation = $ruleConfiguration
            }
        }
    }

    Context "When top level indentation is not consistent" {
        It "Should detect a violation" {
            $def = @'
 function foo ($param1)
{

}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
        }
    }

    Context "When nested indenation is not consistent" {
        It "Should find a violation" {
            $def = @'
function foo ($param1)
{
"abc"
}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
        }
    }

    Context "When a multi-line hashtable is provided" {
        It "Should find violations" {
            $def = @'
$hashtable = @{
a = 1
b = 2
    c = 3
}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 2
        }
    }

    Context "When a multi-line array is provided" {
        It "Should find violations" {
            $def = @'
$array = @(
1,
    2,
3)
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 2
        }
    }

    Context "When a param block is provided" {
        It "Should find violations" {
            $def = @'
param(
            [string] $param1,

[string]
    $param2,

        [string]
$param3
)
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 4
        }
    }

    Context 'LParen indentation' {
        It 'Should preserve script when line starts with LParen' {
            $IdempotentScriptDefinition = @'
function test {
    (foo | bar {
        baz
    })
    Do-Something
}
'@
            Invoke-Formatter -ScriptDefinition $IdempotentScriptDefinition | Should -Be $idempotentScriptDefinition
        }

            It 'Should preserve script when line starts and ends with LParen' {
                $IdempotentScriptDefinition = @'
function test {
    (
        foo | bar {
            baz
        }
    )
    Do-Something
}
'@
                Invoke-Formatter -ScriptDefinition $IdempotentScriptDefinition | Should -Be $idempotentScriptDefinition
            }

            It 'Should preserve script when line starts and ends with LParen but trailing comment' {
                $IdempotentScriptDefinition = @'
function test {
    ( # comment
        foo | bar {
            baz
        }
    )
    Do-Something
}
'@
                Invoke-Formatter -ScriptDefinition $IdempotentScriptDefinition | Should -Be $idempotentScriptDefinition
            }

        It 'Should preserve script when there is Newline after LParen' {
            $IdempotentScriptDefinition = @'
function test {
    $result = (
        Get-Something
    ).Property
    Do-Something
}
'@
            Invoke-Formatter -ScriptDefinition $IdempotentScriptDefinition | Should -Be $idempotentScriptDefinition
        }

    It 'Should preserve script when there is a comment and Newline after LParen' {
        $IdempotentScriptDefinition = @'
function test {
    $result = ( # comment
        Get-Something
    ).Property
    Do-Something
}
'@
        Invoke-Formatter -ScriptDefinition $IdempotentScriptDefinition | Should -Be $idempotentScriptDefinition
    }

    It 'Should find violation in script when LParen is first token on a line and is not followed by Newline' {
        $ScriptDefinition = @'
     (foo)
     (bar)
'@
        $ExpectedScriptDefinition = @'
(foo)
(bar)
'@
    Invoke-FormatterAssertion $ScriptDefinition $ExpectedScriptDefinition 2 $settings
    }
}

    Context "When a sub-expression is provided" {
        It "Should not find a violations" {
            $def = @'
function foo {
    $x = $("abc")
    $x
}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 0
        }
    }

    Context "When a multi-line command is given" {

        It "When a comment is in the middle of a multi-line statement with preceding and succeeding line continuations" {
            $scriptDefinition = @'
foo `
# comment
-bar `
-baz
'@
            $expected = @'
foo `
    # comment
    -bar `
    -baz
'@
            Invoke-FormatterAssertion $scriptDefinition $expected 3 $settings
        }

        It "When a comment is in the middle of a multi-line statement with preceding pipeline and succeeding line continuation " {
            $scriptDefinition = @'
foo |
# comment
bar `
-baz
'@
            $expected = @'
foo |
    # comment
    bar `
        -baz
'@
            Invoke-FormatterAssertion $scriptDefinition $expected 3 $settings
        }

        It "When a comment is after a pipeline and before the newline " {
            $scriptDefinition = @'
foo | # comment
bar
'@
            $expected = @'
foo | # comment
    bar
'@
            Invoke-FormatterAssertion $scriptDefinition $expected 1 $settings
        }

        It "Should find a violation if a pipleline element is not indented correctly" {
            $def = @'
get-process |
where-object {$_.Name -match 'powershell'}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
        }

        It "Should not find a violation if a pipleline element is indented correctly" {
            $def = @'
get-process |
    where-object {
        $_.Name -match 'powershell'
    }
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 0
        }

        It "Should ignore comment in the pipleline" {
            $def = @'
  get-process |
    where-object Name -match 'powershell' | # only this is indented correctly
select Name,Id |
       format-list
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 3
        }

        It "Should indent properly after line continuation (backtick) character" {
            $def = @'
$x = "this " + `
"Should be indented properly"
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
            $params = @{
                RawContent       = $def
                DiagnosticRecord = $violations[0]
                CorrectionsCount = 1
                ViolationText    = "`"Should be indented properly`""
                CorrectionText   = (New-Object -TypeName String -ArgumentList $indentationUnit, $indentationSize) + "`"Should be indented properly`""
            }
            Test-CorrectionExtentFromContent @params
        }

        It "Should indent pipelines correctly using IncreaseIndentationAfterEveryPipeline option" {
            $def = @'
foo |
    bar |
baz
'@
            $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
            $params = @{
                RawContent       = $def
                DiagnosticRecord = $violations[0]
                CorrectionsCount = 1
                ViolationText    = "baz"
                CorrectionText   = (New-Object -TypeName String -ArgumentList $indentationUnit, ($indentationSize * 2)) + 'baz'
            }
            Test-CorrectionExtentFromContent @params
        }

        It "Should indent hashtable correctly using <PipelineIndentation> option" -TestCases @(
            @{
                PipelineIndentation = 'IncreaseIndentationForFirstPipeline'
            },
            @{
                PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline'
            },
            @{
                PipelineIndentation = 'NoIndentation'
            }
            @{
                PipelineIndentation = 'None'
            }
        ) {
            Param([string] $PipelineIndentation)
            $scriptDefinition = @'
@{
        foo = "value1"
    bar = "value2"
}
'@
            $settings = @{
                IncludeRules = @('PSUseConsistentIndentation')
                Rules = @{ PSUseConsistentIndentation = @{ Enable = $true; PipelineIndentation = $PipelineIndentation } }
            }
            Invoke-Formatter -Settings $settings -ScriptDefinition $scriptDefinition | Should -Be @'
@{
    foo = "value1"
    bar = "value2"
}
'@

        }

        It "Should indent pipelines correctly using <PipelineIndentation> option" -TestCases @(
            @{
                PipelineIndentation = 'IncreaseIndentationForFirstPipeline'
                ExpectCorrection    = $true
            },
            @{
                PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline'
                ExpectCorrection    = $true
            },
            @{
                PipelineIndentation = 'NoIndentation'
                ExpectCorrection    = $false
            }
            @{
                PipelineIndentation = 'None'
                ExpectCorrection    = $false
            }
        ) {
            Param([string] $PipelineIndentation, [bool] $ExpectCorrection)
            $def = @'
foo | bar |
baz
'@
            $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = $PipelineIndentation
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            if ($ExpectCorrection) {
                $violations.Count | Should -Be 1
                $params = @{
                    RawContent       = $def
                    DiagnosticRecord = $violations[0]
                    CorrectionsCount = 1
                    ViolationText    = "baz"
                    CorrectionText   = $indentationUnit * $indentationSize + 'baz'
                }
                Test-CorrectionExtentFromContent @params
            }
            else
            {
                $violations | Should -BeNullOrEmpty
            }
        }

        It 'Should preserve script when using PipelineIndentation None' -TestCases @(
            @{ IdempotentScriptDefinition = @'
foo |
bar
'@
            }
            @{ IdempotentScriptDefinition = @'
foo |
    bar
'@
            },
            @{ IdempotentScriptDefinition = @'
foo |
    bar -Parameter1
'@
            },
            @{ IdempotentScriptDefinition = @'
Get-TransportRule |
Where-Object @{ $_.name -match "a"} |
Select-Object @{ E = $SenderDomainIs | Sort-Object }
Foreach-Object { $_.FullName }
'@
            }
            ) {
        param ($IdempotentScriptDefinition)

        $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = 'None'
        Invoke-Formatter -ScriptDefinition $IdempotentScriptDefinition -Settings $settings | Should -Be $idempotentScriptDefinition
    }

        It 'Should preserve script when using PipelineIndentation IncreaseIndentationAfterEveryPipeline' -TestCases @(
            @{ PipelineIndentation = 'IncreaseIndentationForFirstPipeline' }
            @{ PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline' }
            ) {
        param ($PipelineIndentation)
            $IdempotentScriptDefinition = @'
Get-TransportRule |
    Select-Object @{ Key = $SenderDomainIs | Sort-Object }
baz
'@
            $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = $PipelineIndentation
            Invoke-Formatter -ScriptDefinition $IdempotentScriptDefinition -Settings $settings | Should -Be $idempotentScriptDefinition
        }

        It "Should preserve script when using PipelineIndentation <PipelineIndentation>" -TestCases @(
                @{ PipelineIndentation = 'IncreaseIndentationForFirstPipeline' }
                @{ PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline' }
                @{ PipelineIndentation = 'NoIndentation' }
                @{ PipelineIndentation = 'None' }
                ) {
            param ($PipelineIndentation)
            $idempotentScriptDefinition = @'
function hello {
    if ($true) {
        "hello" | Out-Host
    }
}
'@
            $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = $PipelineIndentation
            Invoke-Formatter -ScriptDefinition $idempotentScriptDefinition -Settings $settings | Should -Be $idempotentScriptDefinition
        }

        It "Should preserve script when using PipelineIndentation <PipelineIndentation>" -TestCases @(
            @{ PipelineIndentation = 'IncreaseIndentationForFirstPipeline' }
            @{ PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline' }
            @{ PipelineIndentation = 'NoIndentation' }
            @{ PipelineIndentation = 'None' }
            ) {
        param ($PipelineIndentation)
        $idempotentScriptDefinition = @'
function foo {
    function bar {
        Invoke-Something | ForEach-Object {
        }
    }
}
'@
        $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = $PipelineIndentation
        Invoke-Formatter -ScriptDefinition $idempotentScriptDefinition -Settings $settings | Should -Be $idempotentScriptDefinition
    }

    It "Should preserve script when using PipelineIndentation <PipelineIndentation> for multi-line pipeline due to backtick" -TestCases @(
        @{ PipelineIndentation = 'IncreaseIndentationForFirstPipeline' }
        @{ PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline' }
        @{ PipelineIndentation = 'NoIndentation' }
        @{ PipelineIndentation = 'None' }
        ) {
    param ($PipelineIndentation)
    $idempotentScriptDefinition = @'
Describe 'describe' {
    It 'it' {
        { 'To be,' -or `
                -not 'to be' } | Should -Be 'the question'
    }
}
'@
    $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = $PipelineIndentation
    Invoke-Formatter -ScriptDefinition $idempotentScriptDefinition -Settings $settings | Should -Be $idempotentScriptDefinition
}
        It "Should preserve script when using PipelineIndentation <PipelineIndentation> for complex multi-line pipeline" -TestCases @(
            @{ PipelineIndentation = 'IncreaseIndentationForFirstPipeline' }
            @{ PipelineIndentation = 'IncreaseIndentationAfterEveryPipeline' }
            @{ PipelineIndentation = 'NoIndentation' }
            @{ PipelineIndentation = 'None' }
        ) {
            param ($PipelineIndentation)
            $idempotentScriptDefinition = @'
function foo {
    bar | baz {
        Get-Item
    } | Invoke-Item
    $iShouldStayAtTheSameIndentationLevel
}
'@
            $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = $PipelineIndentation
            Invoke-Formatter -ScriptDefinition $idempotentScriptDefinition -Settings $settings | Should -Be $idempotentScriptDefinition
        }

        It "Should indent pipelines correctly using NoIndentation option" {
            $def = @'
foo |
    bar |
        baz
'@
            $settings.Rules.PSUseConsistentIndentation.PipelineIndentation = 'NoIndentation'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 2
            $params = @{
                RawContent       = $def
                DiagnosticRecord = $violations[1]
                CorrectionsCount = 1
                ViolationText    = "        baz"
                CorrectionText   = (New-Object -TypeName String -ArgumentList $indentationUnit, ($indentationSize * 0)) + 'baz'
            }
            Test-CorrectionExtentFromContent @params
        }

        It "Should indent properly after line continuation (backtick) character with pipeline" {
            $def = @'
foo |
    bar `
| baz
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
            $params = @{
                RawContent       = $def
                DiagnosticRecord = $violations[0]
                CorrectionsCount = 1
                ViolationText    = "| baz"
                CorrectionText   = (New-Object -TypeName String -ArgumentList $indentationUnit, $indentationSize) + "| baz"
            }
            Test-CorrectionExtentFromContent @params
        }
    }

    Context "When tabs instead of spaces are used for indentation" {
        BeforeEach {
            $settings.Rules.PSUseConsistentIndentation.Kind = 'tab'
        }

        It "Should indent using tabs" {
            $def = @'
function foo
{
get-childitem
$x=1+2
$hashtable = @{
property1 = "value"

'@ + "`t" + @'
anotherProperty = "another value"
}
}
'@
            ${t} = "`t"
            $expected = @"
function foo
{
${t}get-childitem
${t}`$x=1+2
${t}`$hashtable = @{
${t}${t}property1 = "value"
${t}${t}anotherProperty = "another value"
${t}}
}
"@
            Invoke-Formatter -ScriptDefinition $def -Settings $settings | Should -Be $expected
        }
    }
}

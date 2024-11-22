# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $testRootDirectory = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $testRootDirectory "PSScriptAnalyzerTestHelper.psm1")

    $ruleName = "PSUseConsistentWhitespace"
    $ruleConfiguration = @{
        Enable          = $true
        CheckInnerBrace = $false
        CheckOpenBrace  = $false
        CheckOpenParen  = $false
        CheckOperator   = $false
        CheckPipe       = $false
        CheckSeparator  = $false
        CheckParameter  = $false
    }

    $settings = @{
        IncludeRules = @($ruleName)
        Rules        = @{
            PSUseConsistentWhitespace = $ruleConfiguration
        }
    }
}


Describe "UseWhitespace" {
    Context "When an open brace follows a keyword" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenBrace = $true
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $false
        }

        It "Should find a violation if an open brace does not follow whitespace" {
            $def = 'if ($true){}'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should not find violation if an open brace follows a whitespace" {
            $def = 'if($true) {}'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It "Should not find violation if an open brace follows a foreach member invocation" {
            $def = '(1..5).foreach{$_}'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It "Should not find violation if an open brace follows a where member invocation" {
            $def = '(1..5).where{$_}'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It "Should not find violation if an open brace is on the next line" {
            $def = @'
if ($true)
{
    foo
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It 'Should not find a violation if an open paren is before an opening brace' {
            Invoke-ScriptAnalyzer -ScriptDefinition '$ast.Find({ $oneAst -is [TypeExpressionAst] })' -Settings $settings |
                Should -BeNullOrEmpty
        }

        It 'Should not find a violation if an open paren is preceded by a Dot token' {
            Invoke-ScriptAnalyzer -ScriptDefinition '$foo.{bar}' -Settings $settings |
                Should -BeNullOrEmpty
        }
    }

    Context "When a parenthesis follows a keyword" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOpenParen = $true
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $false
        }

        It "Should find violation in an if statement" {
            $def = 'if($true) {}'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should not find a violation in a function definition" {
            $def = @'
function foo($param1) {

}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation in a param block" {
            $def = @'
function foo() {
    param( )
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation in a nested open paren" {
            $def = @'
function foo($param) {
    ((Get-Process))
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation on a method call" {
            $def = '$x.foo("bar")'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }
    }

    Context "When there is whitespace around assignment and binary operators" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOperator = $true
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $false
            $ruleConfiguration.IgnoreAssignmentOperatorInsideHashTable = $false
        }

        It "Should find a violation if no whitespace around an assignment operator" {
            $def = '$x=1'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '=' ' = '
        }

        It "Should find a violation if no whitespace before an assignment operator" {
            $def = '$x= 1'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if no whitespace after an assignment operator" {
            $def = '$x =1'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if there is a whitespaces not of size 1 around an assignment operator" {
            $def = '$x  =  1'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  =  ' ' = '
        }

        It "Should not find violation if there are whitespaces of size 1 around an assignment operator" {
            $def = @'
$x = @"
"abc"
"@
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find violation if there are whitespaces of size 1 around an assignment operator for here string" {
            $def = '$x = 1'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find violation if there are no whitespaces around DotDot operator" {
            $def = '1..5'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find violation if a binary operator is followed by new line" {
            $def = @'
$x = $true -and
            $false
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It 'Should not find violation if there are whitespaces of size 1 around a unary operator starting with a dash' {
            Invoke-ScriptAnalyzer -ScriptDefinition '$x -join $y' -Settings $settings | Should -BeNullOrEmpty
        }

        It 'Should find a violation if no whitespace around a unary operator starting with a dash' {
            $def = '$x=1'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '=' ' = '
        }

        It 'Should find a violation if no whitespace before a unary operator starting with a dash' {
            $def = '$x-join $Y'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It 'Should find a violation if no whitespace after a unary operator starting with a dash' {
            $def = '$x -join$y'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It 'Should find a violation if there is a whitespaces not of size 1 around a unary operator starting with a dash' {
            $def = '$x  -join  $y'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  -join  ' ' -join '
        }

        It 'Should find a violation if there is no whitespace after a unary operator with a dash but nothing that preceds it' {
            $def = '-join$x'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '-join' '-join '
        }

        It "Should find violation if not asked to ignore assignment operator in hash table" {
            $def = @'
$ht = @{
    variable = 3
    other    = 4
}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '    ' ' '
        }
    }

    Context "When asked to ignore assignment operator inside hash table" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOperator = $true
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $false
            $ruleConfiguration.IgnoreAssignmentOperatorInsideHashTable = $true
        }

        It "Should not find violation if assignment operator is in multi-line hash table" {
            $def = @'
$ht = @{
    variable = 3
    other    = 4
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should find violation if assignment operator has extra space in single-line hash table" {
            $def = @'
$h = @{
    ht = @{a = 3; b   = 4}
    eb = 33
}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '   ' ' '
        }

        It "Should find violation for extra space around non-assignment operator inside hash table" {
            $def = @'
$ht = @{
    variable = 3
    other    = 4 +  7
}
'@
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  ' ' '
        }
    }

    Context "When a comma is not followed by a space" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $true
        }

        It "Should find a violation" {
            $def = '$x = @(1,2)'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should not find a violation if a space follows a comma" {
            $def = '$x = @(1, 2)'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }
    }

    Context "When a semi-colon is not followed by a space" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $true
        }

        It "Should find a violation" {
            $def = '$x = @{a=1;b=2}'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should not find a violation if a space follows a semi-colon" {
            $def = '$x = @{a=1; b=2}'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a new-line follows a semi-colon" {
            $def = @'
$x = @{
    a=1;
    b=2
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a end of input follows a semi-colon" {
            $def = @'
$x = "abc";
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }
    }


    Context "CheckPipe" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $true
            $ruleConfiguration.CheckPipeForRedundantWhitespace = $false
            $ruleConfiguration.CheckSeparator = $false
        }

        It "Should find a violation if there is no space after pipe" {
            $def = 'Get-Item |foo'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should not find a violation if there is no space before pipe" {
            $def = 'Get-Item| foo'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should not find a violation if there is one space too much before pipe" {
            $def = 'Get-Item  | foo'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It "Should find a violation if there is one space too much after pipe" {
            $def = 'Get-Item |  foo'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It "Should not find a violation if there is 1 space before and after a pipe" {
            $def = 'Get-Item | foo'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a backtick is before the pipe" {
            $def = @'
Get-Item `
| foo
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a new-line is after the pipe" {
            $def = @'
Get-Item |
    foo
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a backtick is after the pipe" {
            $def = @'
Get-Item |`
foo
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }
    }

    Context "CheckPipeForRedundantWhitespace" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $false
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckPipeForRedundantWhitespace = $true
            $ruleConfiguration.CheckSeparator = $false
        }

        It "Should not find a violation if there is no space around pipe" {
            $def = 'foo|bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It "Should not find a violation if there is exactly one space around pipe" {
            $def = 'foo | bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -BeNullOrEmpty
        }

        It "Should find a violation if there is one space too much before pipe" {
            $def = 'foo  | bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  ' ' '
        }

        It "Should find a violation if there is two spaces too much before pipe" {
            $def = 'foo   | bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '   ' ' '
        }

        It "Should find a violation if there is one space too much after pipe" {
            $def = 'foo |  bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  ' ' '
        }

        It "Should find a violation if there is two spaces too much after pipe" {
            $def = 'foo |   bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '   ' ' '
        }
    }

    Context "CheckInnerBrace" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $true
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $false
        }

        It "Should find a violation if there is no space after opening brace" {
            $def = 'if ($true) {Get-Item }'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if there is no space after opening brace when there are 2 braces" {
            $def = 'if ($true) {{ Get-Item } }'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if there is no space before closing brace" {
            $def = 'if ($true) { Get-Item}'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if there is no space before closing brace when there are 2 braces" {
            $def = 'if ($true) { { Get-Item }}'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if there is more than 1 space after opening brace" {
            $def = 'if($true) {  Get-Item }'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  ' ' '
        }

        It "Should find a violation if there is more than 1 space before closing brace" {
            $def = 'if($true) { Get-Item  }'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  ' ' '
        }

        It "Should not find a violation if there is 1 space after the opening brace and 1 before the closing brace" {
            $def = 'if($true) { Get-Item }'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if there is 1 space inside empty curly braces" {
            $def = 'if($true) { }'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation for an empty hashtable" {
            $def = '$hashtable = @{}'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a new-line is after the opening brace" {
            $def = @'
if ($true) {
    Get-Item }
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a backtick is after the opening brace" {
            $def = @'
if ($true) {`
    Get-Item }
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a new-line is before the closing brace" {
            $def = @'
if ($true) { Get-Item
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation if a backtick is before the closing brace" {
            $def = @'
if ($true) { Get-Item `
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It 'Should not throw when analysing a line starting with a scriptblock' {
            { Invoke-ScriptAnalyzer -ScriptDefinition '{ }' -Settings $settings -ErrorAction Stop } | Should -Not -Throw
        }
    }


    Context "CheckParameter" {
        BeforeAll {
            $ruleConfiguration.CheckInnerBrace = $true
            $ruleConfiguration.CheckOpenBrace = $false
            $ruleConfiguration.CheckOpenParen = $false
            $ruleConfiguration.CheckOperator = $false
            $ruleConfiguration.CheckPipe = $false
            $ruleConfiguration.CheckSeparator = $false
            $ruleConfiguration.CheckParameter = $true
        }

        It "Should not find no violation when newlines are involved" {
            $def = {foo -a $b `
-c d -d $e -f g `
-h i |
bar -h i `
-switch}
            Invoke-ScriptAnalyzer -ScriptDefinition "$def" -Settings $settings | Should -Be $null
        }

        It "Should not find no violation if there is always 1 space between parameters except when using colon syntax" {
            $def = 'foo -bar $baz @splattedVariable -bat -parameterName:$parameterValue'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find a violation when redirect operators, spearated by 1 space, are used and not in stream order" {
            # Related to Issue #2000
            $def = 'foo 3>&1 1>$null 2>&1'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should find 1 violation if there is 1 space too much before a parameter" {
            $def = 'foo  -bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
            $violations[0].Extent.Text | Should -Be 'foo'
            $violations[0].SuggestedCorrections[0].Text | Should -Be ([string]::Empty)
        }

        It "Should find 1 violation if there is 1 space too much before a parameter value" {
            $def = 'foo  $bar'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
            $violations[0].Extent.Text | Should -Be 'foo'
            $violations[0].SuggestedCorrections[0].Text | Should -Be ([string]::Empty)
        }

        It "Should fix script to always have 1 space between parameters except when using colon syntax but not by default" {
            $def = 'foo  -bar   $baz  -ParameterName:  $ParameterValue "$PSScriptRoot\module.psd1"'
            Invoke-Formatter -ScriptDefinition $def |
                Should -BeExactly $def -Because 'CheckParameter configuration is not turned on by default (yet) as the setting is new'
            Invoke-Formatter -ScriptDefinition $def -Settings $settings |
                Should -BeExactly 'foo -bar $baz -ParameterName:  $ParameterValue "$PSScriptRoot\module.psd1"'
        }

        It "Should fix script when newlines are involved" {
            $def = {foo  -a  $b `
-c  d -d  $e  -f  g `
-h  i |
bar  -h  i `
-switch}
            $expected = {foo -a $b `
-c d -d $e -f g `
-h i |
bar -h i `
-switch}
            Invoke-Formatter -ScriptDefinition "$def" -Settings $settings |
                Should -Be "$expected"
        }

        It "Should fix script when redirects are involved and whitespace is not consistent" {
            # Related to Issue #2000
            $def = 'foo   3>&1  1>$null   2>&1'
            $expected = 'foo 3>&1 1>$null 2>&1'
            Invoke-Formatter -ScriptDefinition $def -Settings $settings |
                Should -Be $expected
        }
    }
}

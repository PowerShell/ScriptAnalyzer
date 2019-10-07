﻿$directory = Split-Path -Parent $MyInvocation.MyCommand.Path
$testRootDirectory = Split-Path -Parent $directory

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
}

$settings = @{
    IncludeRules = @($ruleName)
    Rules        = @{
        PSUseConsistentWhitespace = $ruleConfiguration
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
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find violation if an open brace follows a foreach member invocation" {
            $def = '(1..5).foreach{$_}'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
        }

        It "Should not find violation if an open brace follows a where member invocation" {
            $def = '(1..5).where{$_}'
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Should -Be $null
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
            $ruleConfiguration.CheckSeparator = $false
        }

        It "Should find a violation if there is no space after pipe" {
            $def = 'Get-Item |foo'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if there is no space before pipe" {
            $def = 'Get-Item| foo'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '' ' '
        }

        It "Should find a violation if there is one space too much before pipe" {
            $def = 'Get-Item  | foo'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  ' ' '
        }

        It "Should find a violation if there is one space too much after pipe" {
            $def = 'Get-Item |  foo'
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            Test-CorrectionExtentFromContent $def $violations 1 '  ' ' '
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
            $def = 'if($true) {  Get-Item }'
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
    }

}

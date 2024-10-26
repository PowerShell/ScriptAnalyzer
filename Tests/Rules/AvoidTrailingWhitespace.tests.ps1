# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $testRootDirectory = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $testRootDirectory "PSScriptAnalyzerTestHelper.psm1")

    $ruleName = "PSAvoidTrailingWhitespace"

    $settings = @{
        IncludeRules = @($ruleName)
        Rules = @{
            $ruleName = @{}
        }
    }
}

Describe "AvoidTrailingWhitespace" {
    $testCases = @(
        @{
            Type       = 'spaces'
            Whitespace = '     '
        }

        @{
            Type       = 'tabs'
            Whitespace = "`t`t`t"
        }
    )

    It 'Should find a violation when a line contains trailing <Type>' -TestCases $testCases {
        param (
            [string] $Whitespace
        )

        $def = "`$null = `$null$Whitespace"
        $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
        Test-CorrectionExtentFromContent $def $violations 1 $Whitespace ''
    }

    It 'Should be used by Invoke-Formatter, when in settings, replacing trailing <Type>' -TestCases $testCases {
        param (
            [string] $Whitespace
        )
        # Test also guards against regression where single-character lines, with trailing whitespace
        # would be removed entirely. See issues #1757, #1992
        $def = @"
Function Get-Example {
    'Example'$Whitespace
}$Whitespace
"@

        $expected = @"
Function Get-Example {
    'Example'
}
"@
        $formatted = Invoke-Formatter -ScriptDefinition $def -Settings $settings
        $formatted | Should -Be $expected
    }

}
# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $testRootDirectory = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $testRootDirectory 'PSScriptAnalyzerTestHelper.psm1')

    $violationMessage = "The variable 'declaredVar2' is assigned but never used."
    $violationName = "PSUseDeclaredVarsMoreThanAssignments"
    $violations = Invoke-ScriptAnalyzer $PSScriptRoot\UseDeclaredVarsMoreThanAssignments.ps1 | Where-Object {$_.RuleName -eq $violationName}
    $noViolations = Invoke-ScriptAnalyzer $PSScriptRoot\UseDeclaredVarsMoreThanAssignmentsNoViolations.ps1 | Where-Object {$_.RuleName -eq $violationName}
}

Describe "UseDeclaredVarsMoreThanAssignments" {
    Context "When there are violations" {
        It "has 2 use declared vars more than assignments violations" {
            $violations.Count | Should -Be 2
        }

        It "has the correct description message" {
            $violations[1].Message | Should -Match $violationMessage
        }

        It "flags the variable in the correct scope" {
            $target = @'
function MyFunc1() {
    $a = 1
    $b = 1
    $a + $b
}

function MyFunc2() {
    $a = 1
    $b = 1
    $a + $a
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $target -IncludeRule $violationName | `
            Get-Count | `
            Should -Be 1
        }

        It "flags strongly typed variables" {
            Invoke-ScriptAnalyzer -ScriptDefinition '[string]$s=''mystring''' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 1
        }

        It "does not flag `$InformationPreference variable" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$InformationPreference=Stop' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 0
        }

        It "does not flag `$PSModuleAutoLoadingPreference variable" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$PSModuleAutoLoadingPreference=None' -IncludeRule $violationName | `
            Get-Count | `
            Should -Be 0
        }

        It "does not flag global variable" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$global:x=$null' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 0
        }

        It "does not flag global variable in block" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$global:x=$null;{$global:x=$null}' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 0
        }

        It "does not flag env variable" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$env:x=$null' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 0
        }

        It "does not flag env variable in block" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$env:x=$null;{$env:x=$null}' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 0
        }

        It "does not flag script variable" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$script:x=$null' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 0
        }

        It "does not flag script variable in block" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$script:x=$null;{$script:x=$null}' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 0
        }

        It "flags private variable" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$private:x=$null' -IncludeRule $violationName  | `
            Get-Count | `
            Should -Be 1
        }

        It "flags a variable that is defined twice but never used" {
            Invoke-ScriptAnalyzer -ScriptDefinition '$myvar=1;$myvar=2' -IncludeRule $violationName | `
            Get-Count | `
            Should -Be 1
        }

        It "does not flag a variable that is defined twice but gets assigned to another variable and flags the other variable instead" {
            $results = Invoke-ScriptAnalyzer -ScriptDefinition '$myvar=1;$myvar=2;$mySecondvar=$myvar' -IncludeRule $violationName
            $results | Get-Count | Should -Be 1
            $results[0].Extent | Should -Be '$mySecondvar'
        }
    }

    Context "When there are no violations" {
        It "No warning is issued for assignment without use of preference variable ErrorView" {
            $results = Invoke-ScriptAnalyzer -ScriptDefinition '$ErrorView = NormalView'
            $results.Count | Should -Be 0
        }

        It "returns no violations" {
            $noViolations.Count | Should -Be 0
        }

        It "Does not flag += operator" {
            $results = Invoke-ScriptAnalyzer -ScriptDefinition '$array=@(); $list | ForEach-Object { $array += $c }' | Where-Object { $_.RuleName -eq $violationName }
            $results.Count | Should -Be 0
        }

        It "Does not flag += operator when using unassigned variable" {
            $results = Invoke-ScriptAnalyzer -ScriptDefinition '$list | ForEach-Object { $array += $c }' | Where-Object { $_.RuleName -eq $violationName }
            $results.Count | Should -Be 0
        }

        It "Does not flag drive qualified variables such as env" {
            $results = Invoke-ScriptAnalyzer -ScriptDefinition '$env:foo = 1; function foo(){ $env:bar = 42 }'
            $results.Count | Should -Be 0
        }

        It "No warning when using 'Get-Variable' with variables declaration '<DeclareVariables>' and command parameter <GetVariableCommandParameter>" -TestCases @(
            @{
                DeclareVariables = '$a = 1'; GetVariableCommandParameter = 'a';
            }
            @{
                DeclareVariables = '$a = 1'; GetVariableCommandParameter = '-Name a';
            }
            @{
                DeclareVariables = '$a = 1'; GetVariableCommandParameter = '-n a';
            }
            @{
                DeclareVariables = '$a = 1; $b = 2'; GetVariableCommandParameter = 'a,b'
            }
            @{
                DeclareVariables = '$a = 1; $b = 2'; GetVariableCommandParameter = '-Name a,b'
            }
            @{
                DeclareVariables = '$a = 1; $b = 2'; GetVariableCommandParameter = '-n a,b'
            }
            @{
                DeclareVariables = '$a = 1; $b = 2'; GetVariableCommandParameter = 'A,B'
            }
        ) {
            Param(
                $DeclareVariables,
                $GetVariableCommandParameter
            )
            $scriptDefinition = "$DeclareVariables; Get-Variable $GetVariableCommandParameter"
            $noViolations = Invoke-ScriptAnalyzer -ScriptDefinition $scriptDefinition
            $noViolations.Count | Should -Be 0 -Because $scriptDefinition
        }

        It "Does not misinterpret switch parameter of Get-Variable as variable" {
            $scriptDefinition = '$ArbitrarySwitchParameter = 1; Get-Variable -ArbitrarySwitchParameter'
            (Invoke-ScriptAnalyzer -ScriptDefinition $scriptDefinition).Count | Should -Be 1 -Because $scriptDefinition
        }
    }
}

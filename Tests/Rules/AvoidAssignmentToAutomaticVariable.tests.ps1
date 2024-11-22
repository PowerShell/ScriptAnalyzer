# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $ruleName = "PSAvoidAssignmentToAutomaticVariable"
}

Describe "AvoidAssignmentToAutomaticVariables" {
    Context "ReadOnly Variables" {
        BeforeDiscovery {
            $excpectedSeverityForAutomaticVariablesInPowerShell6 = 'Warning'
            if ($PSVersionTable.PSVersion.Major -ge 6)
            {
                $excpectedSeverityForAutomaticVariablesInPowerShell6 = 'Error'
            }

            $testCases_AutomaticVariables = @(
                @{ VariableName = '?'; ExpectedSeverity = 'Error'; IsReadOnly = $true }
                @{ VariableName = 'Error' ; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'ExecutionContext'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'false'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'Home'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'Host'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'PID'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'PSCulture'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'PSEdition'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'PSHome'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'PSUICulture'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'PSVersionTable'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'ShellId'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                @{ VariableName = 'true'; ExpectedSeverity = 'Error';  IsReadOnly = $true }
                # Variables introduced only in PowerShell 6+ have a Severity of Warning only
                @{ VariableName = 'IsCoreCLR'; ExpectedSeverity = $excpectedSeverityForAutomaticVariablesInPowerShell6; OnlyPresentInCoreClr = $true }
                @{ VariableName = 'IsLinux'; ExpectedSeverity = $excpectedSeverityForAutomaticVariablesInPowerShell6; OnlyPresentInCoreClr = $true }
                @{ VariableName = 'IsMacOS'; ExpectedSeverity = $excpectedSeverityForAutomaticVariablesInPowerShell6; OnlyPresentInCoreClr = $true }
                @{ VariableName = 'IsWindows'; ExpectedSeverity = $excpectedSeverityForAutomaticVariablesInPowerShell6; OnlyPresentInCoreClr = $true }
                @{ VariableName = '_'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'AllNodes'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'Args'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'ConsoleFilename'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'Event'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'EventArgs'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'EventSubscriber'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'ForEach'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'Input'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'Matches'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'MyInvocation'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'NestedPromptLevel'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'Profile'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'PSBoundParameters'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'PsCmdlet'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'PSCommandPath'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'ReportErrorShowExceptionClass'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'ReportErrorShowInnerException'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'ReportErrorShowSource'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'ReportErrorShowStackTrace'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'Sender'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'StackTrace'; ExpectedSeverity = 'Warning' }
                @{ VariableName = 'This'; ExpectedSeverity = 'Warning' }
            )

            $testCases_ReadOnlyAutomaticVariables = $testCases_AutomaticVariables | Where-Object { $_.IsReadonly }
        }

        It "Variable <VariableName> produces warning of Severity <ExpectedSeverity>" -TestCases $testCases_AutomaticVariables {
            param ($VariableName, $ExpectedSeverity)

            $warnings = Invoke-ScriptAnalyzer -ScriptDefinition "`$${VariableName} = 'foo'" -ExcludeRule PSUseDeclaredVarsMoreThanAssignments
            $warnings.Count | Should -Be 1
            $warnings.Severity | Should -Be $ExpectedSeverity
            $warnings.RuleName | Should -Be $ruleName
        }

        It "Using Variable <VariableName> as parameter name produces warning of Severity error" -TestCases $testCases_AutomaticVariables {
            param ($VariableName, $ExpectedSeverity)

            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition "function foo{Param(`$$VariableName)}" -ExcludeRule PSReviewUnusedParameter
            $warnings.Count | Should -Be 1
            $warnings.Severity | Should -Be $ExpectedSeverity
            $warnings.RuleName | Should -Be $ruleName
        }

        It "Using Variable <VariableName> as parameter name in param block produces warning of Severity error" -TestCases $testCases_AutomaticVariables {
            param ($VariableName, $ExpectedSeverity)

            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition "function foo(`$$VariableName){}"
            $warnings.Count | Should -Be 1
            $warnings.Severity | Should -Be $ExpectedSeverity
            $warnings.RuleName | Should -Be $ruleName
        }

        It "Does not flag parameter attributes" {
            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition 'function foo{Param([Parameter(Mandatory=$true)]$param1)}' -ExcludeRule PSReviewUnusedParameter
            $warnings.Count | Should -Be 0
        }

        It "Does not flag true or false being used in ValidateSet" {
            # All other read-only automatic variables cannot be used in ValidateSet
            # they result in a ParseError. $true and $false are permitted however.
            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition 'param([ValidateSet($true,$false)]$MyVar)$MyVar' -ExcludeRule PSReviewUnusedParameter
            $warnings.Count | Should -Be 0
        }

        It "Does not throw a NullReferenceException when using assigning a .Net property to a .Net property (Bug in 1.17.0 - issue 1007)" {
            Invoke-ScriptAnalyzer -ScriptDefinition '[foo]::bar = [baz]::qux' -ErrorAction Stop
        }

        It "Does not flag properties of a readonly variable (issue 1012)" {
            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition '$Host.PrivateData["ErrorBackgroundColor"] = "Black"'
            $warnings.Count | Should -Be 0
        }

        It "Does not flag RHS of variable assignment (Bug in 1.17.0, issue 1013)" {
            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition '[foo]::bar = $true'
            $warnings.Count | Should -Be 0
        }

        It "Setting Variable <VariableName> throws exception in applicable PowerShell version to verify the variables is read-only" -TestCases $testCases_ReadOnlyAutomaticVariables {
            param ($VariableName, $ExpectedSeverity, $OnlyPresentInCoreClr, [bool] $IsReadOnly)

            if ($OnlyPresentInCoreClr -and !$IsCoreCLR)
            {
                # In this special case we expect it to not throw
                Set-Variable -Name $VariableName -Value 'foo'
                continue
            }

            # Setting the $Error variable has the side effect of the ErrorVariable to contain only the exception message string, therefore exclude this case.
            # For the library test in WMF 4, assigning a value $PSEdition does not seem to throw an error, therefore this special case is excluded as well.
            if ($VariableName -ne 'Error' -and ($VariableName -ne 'PSEdition' -and $PSVersionTable.PSVersion.Major -ne 4))
            {
                try
                {
                    # Global scope has to be used due to a bug in PS. https://github.com/PowerShell/PowerShell/issues/6378
                    Set-Variable -Name $VariableName -Value 'foo' -ErrorVariable errorVariable -ErrorAction Stop -Scope Global
                    throw "Expected exception did not occur when assigning value to read-only variable '$VariableName'"
                }
                catch
                {
                    $_.FullyQualifiedErrorId | Should -Be 'VariableNotWritable,Microsoft.PowerShell.Commands.SetVariableCommand'
                }
            }
        }
    }

    Context 'Suppression' {
        BeforeDiscovery {
            $testCases_RuleSuppression = @(
                @{ VariableName = 'true'; Type = 'ReadOnlyAutomaticVariableError' }
                @{ VariableName = 'IsWindows'; Type = 'ReadOnlyAutomaticVariableIntroducedInPowerShell6_0Error' }
                @{ VariableName = 'ForEach'; Type = 'WritableAutomaticVariableError' }
            )
        }

        It 'Variable <VariableName> of type <Type> can be suppressed by RuleSuppressionId' -TestCases $testCases_RuleSuppression {
            $scriptDef = @"
function suppressionTest {
    [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute('$ruleName', '$VariableName')]
    param(
        `$$VariableName
    )
}
"@

            $warnings = @(Invoke-ScriptAnalyzer -ScriptDefinition $scriptDef -ExcludeRule PSReviewUnusedParameter)
            $warnings.Count | Should -Be 0
        }
    }
}

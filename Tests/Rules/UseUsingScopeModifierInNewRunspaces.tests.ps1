# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $settings = @{
        IncludeRules = "PSUseUsingScopeModifierInNewRunspaces"
        Severity     = "warning" # because we need to prevent ParseErrors from being reported, so 'workflow' keyword will not be flagged when running test on Pwsh.
    }
}

Describe "UseUsingScopeModifierInNewRunspaces" {
    Context "Should detect something" {
        It "should emit for: <Description>" {
            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition $ScriptBlock -Settings $settings
            $warnings.Count | Should -Be 1
        } -TestCases @(
            # Test: Foreach-Object -Parallel {}
            @{
                Description = "Foreach-Object -Parallel with undeclared var"
                ScriptBlock = '{
                    1..2 | ForEach-Object -Parallel { $var }
                }'
            }
            @{
                Description = "foreach -parallel alias with undeclared var"
                ScriptBlock = '{
                    1..2 | ForEach -Parallel { $var }
                }'
            }
            @{
                Description = "% -parallel alias with undeclared var"
                ScriptBlock = '{
                    1..2 | % -Parallel { $var }
                }'
            }
            @{
                Description = "Foreach-Object -pa abbreviated param with undeclared var"
                ScriptBlock = '{
                    1..2 | foreach-object -pa { $var }
                }'
            }
            @{
                Description = "Foreach-Object -Parallel nested with undeclared var"
                ScriptBlock = '{
                    $myNestedScriptBlock = {
                        1..2 | ForEach-Object -Parallel { $var }
                    }
                }'
            }
            # Start-Job / Start-ThreadJob
            @{
                Description = 'Start-Job without $using:'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-Job {$foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            @{
                Description = 'Start-ThreadJob without $using:'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-ThreadJob -ScriptBlock {$foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            @{
                Description = 'Start-Job with -InitializationScript with a variable'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-Job -ScriptBlock {$foo} -InitializationScript {$foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            @{
                Description = 'Start-ThreadJob with -InitializationScript with a variable'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-ThreadJob -ScriptBlock {$foo} -InitializationScript {$foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            # workflow/inlinescript
            @{
                Description = "Workflow/InlineScript"
                ScriptBlock = '{
                    $foo = "bar"
                    workflow baz { InlineScript {$foo} }
                }'
            }
            # Invoke-Command
            @{
                Description = 'Invoke-Command with -ComputerName'
                ScriptBlock = '{
                    Invoke-Command -ScriptBlock {Write-Output $foo} -ComputerName "bar"
                }'
            }
            @{
                Description = 'Invoke-Command with two different sessions, where var is declared in wrong session'
                ScriptBlock = '{
                    $session = new-PSSession -ComputerName "baz"
                    $otherSession = new-PSSession -ComputerName "bar"
                    Invoke-Command -session $session -ScriptBlock {[string]$foo = "foo" }
                    Invoke-Command -session $otherSession -ScriptBlock {Write-Output $foo}
                }'
            }
            @{
                Description = 'Invoke-Command with session, where var is declared after use'
                ScriptBlock = '{
                    $session = new-PSSession -ComputerName "baz"
                    Invoke-Command -session $session -ScriptBlock {Write-Output $foo}
                    Invoke-Command -session $session -ScriptBlock {$foo = "foo" }
                }'
            }
            # DSC Script resource
            @{
                Description = 'DSC Script resource with GetScript {}'
                ScriptBlock = 'Script ReturnFoo {
                        GetScript = {
                            return @{ "Result" = "$foo" }
                        }
                    }'
            }
            @{
                Description = 'DSC Script resource with TestScript {}'
                ScriptBlock = 'Script TestFoo {
                        TestScript = {
                            return [bool]$foo
                        }
                    }'
            }
            @{
                Description = 'DSC Script resource with SetScript {}'
                ScriptBlock = 'Script SetFoo {
                        SetScript = {
                            $foo | Set-Content -path "~\nonexistent\foo.txt"
                        }
                    }'
            }
        )

        It "should emit suggested correction" {
            $ScriptBlock = '{
                1..2 | ForEach-Object -Parallel { $var }
            }'
            $warnings = Invoke-ScriptAnalyzer -ScriptDefinition $ScriptBlock -Settings $settings

            $warnings[0].SuggestedCorrections[0].Text | Should -Be '$using:var'
        }
    }

    Context "Should not detect anything" {
        It "should not emit anything for: <Description>" {
            [System.Array] $warnings = Invoke-ScriptAnalyzer -ScriptDefinition $ScriptBlock -Settings $settings
            $warnings.Count | Should -Be 0
        } -TestCases @(
            @{
                Description = "Foreach-Object with uninitialized var inside"
                ScriptBlock = '{
                    1..2 | ForEach-Object { $var }
                }'
            }
            @{
                Description = "Foreach-Object -Parallel with uninitialized `$using: var"
                ScriptBlock = '{
                    1..2 | foreach-object -Parallel { $using:var }
                }'
            }
            @{
                Description = "Foreach-Object -Parallel with var assigned locally"
                ScriptBlock = '{
                    1..2 | ForEach-Object -Parallel { [string]$var="somevalue" }
                }'
            }
            @{
                Description = "Foreach-Object -Parallel with built-in var '`$PSBoundParameters' inside"
                ScriptBlock = '{
                    1..2 | ForEach-Object -Parallel{ $PSBoundParameters }
                }'
            }
            @{
                Description = "Foreach-Object -Parallel with vars in other parameters"
                ScriptBlock = '{
                    $foo = "bar"
                    ForEach-Object -Parallel {$_} -InputObject $foo
                }'
            }
            # Start-Job / Start-ThreadJob
            @{
                Description = 'Start-Job with $using:'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-Job -ScriptBlock {$using:foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            @{
                Description = 'Start-ThreadJob with $using:'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-ThreadJob -ScriptBlock {$using:foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            @{
                Description = 'Start-Job with -InitializationScript with a variable'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-Job -ScriptBlock {$using:foo} -InitializationScript {$foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            @{
                Description = 'Start-ThreadJob with -InitializationScript with a variable'
                ScriptBlock = '{
                    $foo = "bar"
                    Start-ThreadJob -ScriptBlock {$using:foo} -InitializationScript {$foo} | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            # workflow/inlinescript
            @{
                Description = "Workflow/InlineScript"
                ScriptBlock = '{
                    $foo = "bar"
                    workflow baz { InlineScript {$using:foo} }
                }'
            }
            # Invoke-Command
            @{
                Description = 'Invoke-Command -Session, var declared in same session, other scriptblock'
                ScriptBlock = '{
                    $session = new-PSSession -ComputerName "baz"
                    Invoke-Command -session $session -ScriptBlock {$foo = "foo" }
                    Invoke-Command -session $session -ScriptBlock {Write-Output $foo}
                }'
            }
            @{
                Description = 'Invoke-Command without -ComputerName'
                ScriptBlock = '{
                    Invoke-Command -ScriptBlock {Write-Output $foo}
                }'
            }
            # Unsupported scenarios
            @{
                Description = 'Rule should skip analysis when Command Name cannot be resolved'
                ScriptBlock = '{
                    $commandName = "Invoke-Command"
                    & $commandName -ComputerName -ScriptBlock { $foo }
                }'
            }
            # DSC Script resource
            @{
                Description = 'DSC Script resource with GetScript {}'
                ScriptBlock = 'Script ReturnFoo {
                    GetScript = {
                        return @{ "Result" = "$using:foo" }
                    }
                }'
            }
            @{
                Description = 'DSC Script resource with TestScript {}'
                ScriptBlock = 'Script TestFoo {
                    TestScript = {
                        return [bool]$using:foo
                    }
                }'
            }
            @{
                Description = 'DSC Script resource with SetScript {}'
                ScriptBlock = 'Script SetFoo {
                    SetScript = {
                        $using:foo | Set-Content -path "~\nonexistent\foo.txt"
                    }
                }'
            }
            @{
                Description = 'Non-DSC function with the name SetScript {}'
                ScriptBlock = '{
                    SetScript -ScriptBlock {
                        $foo | Set-Content -path "~\nonexistent\foo.txt"
                    }
                }'
            }
            # Issue 1492: https://github.com/PowerShell/PSScriptAnalyzer/issues/1492
            @{
                Description = 'Does not throw when the same variable name is used in two different sessions'
                ScriptBlock = '{
                    function Get-One {
                        Invoke-Command -Session -ScriptBlock $sourceRemoteSession {
                            $a = $sccmModule
                            foo $a
                        }
                    }
                    function Get-Two {
                        Invoke-Command -Session -ScriptBlock $sourceRemoteSession {
                            $a = $sccmModule
                            foo $a
                        }
                    }
                }'
            }
            # ScriptBlock with variables in params(), issue #1504: https://github.com/PowerShell/PSScriptAnalyzer/issues/1504
            ## Microsoft.PowerShell.Core \ Start-Job
            @{
                Description = 'Does not warn when variable is defined inside param() - Start-Job'
                ScriptBlock = '{
                    Start-Job -ScriptBlock {
                        Param($Foo)
                        $Foo
                    } -ArgumentList "Bar" | Receive-Job -Wait -AutoRemoveJob
                }'
            }
            ## Microsoft.PowerShell.ThreadJob \ Start-ThreadJob
            @{
                Description = 'Does not warn when variable is defined inside param() - Start-Job'
                ScriptBlock = '{
                    Start-ThreadJob -ScriptBlock {
                        Param($Foo)
                        $Foo
                    } -ArgumentList "Bar" | Receive-Job -Wait -AutoRemoveJob
                }'
            }
        )
    }
}

# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $settingsTestDirectory = [System.IO.Path]::Combine($PSScriptRoot, "SettingsTest")
    $project1Root = [System.IO.Path]::Combine($settingsTestDirectory, "Project1")
    $project2Root = [System.IO.Path]::Combine($settingsTestDirectory, "Project2")
    $settingsTypeName = 'Microsoft.Windows.PowerShell.ScriptAnalyzer.Settings'
}

Describe "Settings Precedence" {
    Context "settings object is explicit" {
        It "runs rules from the explicit setting file" {
            $settingsFilepath = [System.IO.Path]::Combine($project1Root, "ExplicitSettings.psd1")
            $violations = Invoke-ScriptAnalyzer -Path $project1Root -Settings $settingsFilepath -Recurse
            $violations.Count | Should -Be 2
        }
    }

    Context "settings file is implicit" {
        It "runs rules from the implicit setting file using the -Path parameter set" {
            $violations = Invoke-ScriptAnalyzer -Path $project1Root -Recurse
            $violations.Count | Should -Be 1
            $violations[0].RuleName | Should -Be "PSAvoidUsingCmdletAliases"
        }

        It "runs rules from the implicit setting file using the -ScriptDefinition parameter set" {
            Push-Location $project1Root
            $violations = Invoke-ScriptAnalyzer -ScriptDefinition 'gci; Write-Host' -Recurse
            Pop-Location
            $violations.Count | Should -Be 1
            $violations[0].RuleName | Should -Be "PSAvoidUsingCmdletAliases" `
                -Because 'the implicit settings file should have run only the PSAvoidUsingCmdletAliases rule but not PSAvoidUsingWriteHost'
        }

        It "cannot find file if not named PSScriptAnalyzerSettings.psd1" {
            $violations = Invoke-ScriptAnalyzer -Path $project2Root -Recurse
            $violations.Count | Should -Be 2
        }
    }
}

$customRuleParameterTestCases = @('IncludeDefaultRules', 'RecurseCustomRulePath').ForEach{ @{ ParamName = $_ } }

Describe "Settings Class" {
    Context "When an empty hashtable is provided" {

        It "Should return empty <name> property" -TestCases @(
            @{ Name = "IncludeRules" }
            @{ Name = "ExcludeRules" }
            @{ Name = "Severity" }
            @{ Name = "RuleArguments" }
        ) {
            Param($Name)

            $settings = New-Object -TypeName $settingsTypeName -ArgumentList @{}
            ${settings}.${Name}.Count | Should -Be 0
        }

        It "Should be able to parse empty settings hashtable from settings file" {
            $testPSSASettingsFilePath = "TestDrive:\PSSASettings.psd1"
            Set-Content $testPSSASettingsFilePath -Value '@{ExcludeRules = @()}'
            Invoke-ScriptAnalyzer -ScriptDefinition 'gci' -Settings $testPSSASettingsFilePath | Should -Not -BeNullOrEmpty
        }
    }

    Context "When a string is provided for IncludeRules in a hashtable" {
        BeforeAll {
            $ruleName = "PSAvoidCmdletAliases"
            $settings = New-Object -TypeName $settingsTypeName -ArgumentList @{ IncludeRules = $ruleName }
        }

        It "Should return an IncludeRules array with 1 element" {
            $settings.IncludeRules.Count | Should -Be 1
        }

        It "Should return the rule in the IncludeRules array" {
            $settings.IncludeRules[0] | Should -Be $ruleName
        }
    }

    Context 'When rule arguments are provided in a hashtable' {
        BeforeAll {
            $settingsHashtable = @{
                Rules = @{
                    PSAvoidUsingCmdletAliases = @{
                        AllowList = @('cd', 'cp')
                    }
                }
            }
            $settings = New-Object -TypeName $settingsTypeName -ArgumentList $settingsHashtable
        }

        It 'Should return the rule arguments' {
            $settings.RuleArguments['PSAvoidUsingCmdletAliases']['AllowList'].Count | Should -Be 2
            $settings.RuleArguments['PSAvoidUsingCmdletAliases']['AllowList'][0] | Should -Be 'cd'
            $settings.RuleArguments['PSAvoidUsingCmdletAliases']['AllowList'][1] | Should -Be 'cp'
        }

        It 'Should Be case insensitive' {
            $settings.RuleArguments['psAvoidUsingCmdletAliases']['AllowList'].Count | Should -Be 2
            $settings.RuleArguments['psAvoidUsingCmdletAliases']['AllowList'][0] | Should -Be 'cd'
            $settings.RuleArguments['psAvoidUsingCmdletAliases']['AllowList'][1] | Should -Be 'cp'
        }
    }

    Context 'When rule arguments are provided in a hashtable (legacy argument name)' {
        BeforeAll {
            $settingsHashtable = @{
                Rules = @{
                    PSAvoidUsingCmdletAliases = @{
                        allowlist = @("cd", "cp")
                    }
                }
            }
            $settings = New-Object -TypeName $settingsTypeName -ArgumentList $settingsHashtable
        }

        It "Should return the rule arguments" {
            $settings.RuleArguments["PSAvoidUsingCmdletAliases"]["allowlist"].Count | Should -Be 2
            $settings.RuleArguments["PSAvoidUsingCmdletAliases"]["allowlist"][0] | Should -Be "cd"
            $settings.RuleArguments["PSAvoidUsingCmdletAliases"]["allowlist"][1] | Should -Be "cp"
        }

        It "Should Be case insensitive" {
            $settings.RuleArguments["psAvoidUsingCmdletAliases"]["allowlist"].Count | Should -Be 2
            $settings.RuleArguments["psAvoidUsingCmdletAliases"]["allowlist"][0] | Should -Be "cd"
            $settings.RuleArguments["psAvoidUsingCmdletAliases"]["allowlist"][1] | Should -Be "cp"
        }
    }

    Context "When a settings file path is provided" {
        BeforeAll {
            $settings = New-Object -TypeName $settingsTypeName `
                -ArgumentList ([System.IO.Path]::Combine($project1Root, "ExplicitSettings.psd1"))
            $expectedNumberOfIncludeRules = 3
        }

        It "Should return correct IncludeRules count" {
            $settings.IncludeRules.Count | Should -Be 3
        }

        It "Should return correct ExcludeRules count" {
            $settings.ExcludeRules.Count | Should -Be 3
        }

        It "Should return correct rule argument count" {
            $settings.RuleArguments.Count | Should -Be 3
        }

        It "Should parse boolean type argument" {
            $settings.RuleArguments["PSUseConsistentIndentation"]["Enable"] | Should -BeTrue
        }

        It "Should parse int type argument" {
            $settings.RuleArguments["PSUseConsistentIndentation"]["IndentationSize"] | Should -Be 4
        }

        It "Should parse string literal" {
            $settings.RuleArguments["PSProvideCommentHelp"]["Placement"] | Should -Be 'end'
        }
    }

    Context "When CustomRulePath parameter is provided" {
        It "Should return an array of 1 item when only 1 path is given in a hashtable" {
            $rulePath = "C:\rules\module1"
            $settingsHashtable = @{
                CustomRulePath = $rulePath
            }

            $settings = New-Object -TypeName $settingsTypeName  -ArgumentList $settingsHashtable
            $settings.CustomRulePath.Count | Should -Be 1
            $settings.CustomRulePath[0] | Should -Be $rulePath
        }

        It "Should return an array of n items when n items are given in a hashtable" {
            $rulePaths = @("C:\rules\module1", "C:\rules\module2")
            $settingsHashtable = @{
                CustomRulePath = $rulePaths
            }

            $settings = New-Object -TypeName $settingsTypeName  -ArgumentList $settingsHashtable
            $settings.CustomRulePath.Count | Should -Be $rulePaths.Count
            0..($rulePaths.Count - 1) | ForEach-Object { $settings.CustomRulePath[$_] | Should -Be $rulePaths[$_] }

        }

        It "Should detect the parameter in a settings file" {
            $settings = New-Object -TypeName $settingsTypeName `
                -ArgumentList ([System.IO.Path]::Combine($project1Root, "CustomRulePathSettings.psd1"))
            $settings.CustomRulePath.Count | Should -Be 2
        }
    }

    Context "When custom rule path relates parameters are provided" {
        It "<ParamName>: Should correctly set the value if a boolean is given - true" -TestCases $customRuleParameterTestCases {
            $settingsHashtable = @{}
            $settingsHashtable.Add($ParamName, $true)

            $settings = New-Object -TypeName $settingsTypeName -ArgumentList $settingsHashtable
            $settings."$ParamName" | Should -BeTrue
        }

        It "<ParamName>: Should correctly set the value if a boolean is given - false" -TestCases $customRuleParameterTestCases {
            $settingsHashtable = @{}
            $settingsHashtable.Add($ParamName, $false)

            $settings = New-Object -TypeName $settingsTypeName -ArgumentList $settingsHashtable
            $settings."$ParamName" | Should -BeFalse
        }

        It "<ParamName>: Should throw if a non-boolean value is given" -TestCases $customRuleParameterTestCases {
            $settingsHashtable = @{}
            $settingsHashtable.Add($ParamName, "some random string")

            { New-Object -TypeName $settingsTypeName -ArgumentList $settingsHashtable } | Should -Throw
        }

        It "<ParamName>: Should detect the parameter in a settings file" -TestCases $customRuleParameterTestCases {
            $settings = New-Object -TypeName $settingsTypeName `
                -ArgumentList ([System.IO.Path]::Combine($project1Root, "CustomRulePathSettings.psd1"))
            $settings."$ParamName" | Should -BeTrue
        }
    }

    Context "Settings GetSafeValue API" {
        BeforeAll {
            function ShouldBeDeeplyEqual
            {
                param(
                    [Parameter(Position = 0)]
                    $To,

                    [Parameter(ValueFromPipeline)]
                    $InputObject
                )

                if ($null -eq $To)
                {
                    $InputObject | Should -Be $null
                    return
                }

                if ($To -is [hashtable])
                {
                    foreach ($toKey in $To.get_Keys())
                    {
                        $inputVal = $InputObject[$toKey]
                        if ($inputVal -is [array])
                        {
                            @(, $inputVal) | ShouldBeDeeplyEqual -To $To[$toKey]
                            continue
                        }
                        $inputVal | ShouldBeDeeplyEqual -To $To[$toKey]
                    }
                    return
                }

                if ($To -is [array])
                {
                    $InputObject.Count | Should -Be $To.Count
                    for ($i = 0; $i -lt $To.Count; $i++)
                    {
                        $inputVal = $InputObject[$i]
                        if ($inputVal -is [array])
                        {
                            @(, $inputVal) | ShouldBeDeeplyEqual -To $To[$i]
                            continue
                        }
                        $inputVal | ShouldBeDeeplyEqual -To $To[$i]
                    }
                    return
                }

                $InputObject | Should -Be $To
            }
        }

        It "Safely gets the simple value <Expr>" {
            param([string]$Expr)

            $pwshVal = Invoke-Expression $Expr

            $exprAst = [System.Management.Automation.Language.Parser]::ParseInput($Expr, [ref]$null, [ref]$null)
            $exprAst = $exprAst.Find( { $args[0] -is [System.Management.Automation.Language.ExpressionAst] }, $true)
            $gsvVal = [Microsoft.Windows.PowerShell.ScriptAnalyzer.Helper]::GetSafeValueFromExpressionAst($exprAst)

            $gsvVal | Should -Be $pwshVal
        } -TestCases @(
            @{ Expr = '0' }
            @{ Expr = '-2' }
            @{ Expr = '-2.5' }
            @{ Expr = '$true' }
            @{ Expr = '$false' }
            @{ Expr = '123124' }
            @{ Expr = '0.142' }
            @{ Expr = '"Hello"' }
            @{ Expr = '"Well then"' }
            @{ Expr = '$null' }
        )

        It "Safely gets the array value <Expr>" {
            param([string]$Expr)

            $pwshVal = Invoke-Expression $Expr

            $exprAst = [System.Management.Automation.Language.Parser]::ParseInput($Expr, [ref]$null, [ref]$null)
            $exprAst = $exprAst.Find( { $args[0] -is [System.Management.Automation.Language.ExpressionAst] }, $true)
            $gsvVal = [Microsoft.Windows.PowerShell.ScriptAnalyzer.Helper]::GetSafeValueFromExpressionAst($exprAst)


            # Need to test the type like this so that the pipeline doesn't unwrap the type,
            # but we also don't create the array ourselves
            $gsvVal.GetType().IsArray | Should -BeTrue

            @(, $gsvVal) | ShouldBeDeeplyEqual -To $pwshVal
        } -TestCases @(
            @{ Expr = '1, 2, 3'; Count = 3 }
            @{ Expr = '"One","Two","Three"'; Count = 3 }
            @{ Expr = '@(1,2,3,4)'; Count = 4 }
            @{ Expr = '@("A","B","C")'; Count = 3 }
            @{ Expr = '@()'; Count = 0 }
            @{ Expr = '@(7)'; Count = 1 }
            @{ Expr = "'1',`n'2',`n'3'"; Count = 3 }
            @{ Expr = "@(1`n3`n5`n7)"; Count = 4 }
            @{ Expr = "'1',`r`n'2',`r`n'3'"; Count = 3 }
            @{ Expr = "@(1`r`n3`r`n5`r`n7)"; Count = 4 }
            @{ Expr = "@(1,2,3`n7,8,9)"; Count = 6 }
        )

        It "Safely gets the hashtable value <Expr>" {
            param([string]$Expr)

            $pwshVal = Invoke-Expression $Expr

            $exprAst = [System.Management.Automation.Language.Parser]::ParseInput($Expr, [ref]$null, [ref]$null)
            $exprAst = $exprAst.Find( { $args[0] -is [System.Management.Automation.Language.ExpressionAst] }, $true)
            $gsvVal = [Microsoft.Windows.PowerShell.ScriptAnalyzer.Helper]::GetSafeValueFromExpressionAst($exprAst)

            $gsvVal | Should -BeOfType [hashtable]
            $gsvVal | ShouldBeDeeplyEqual -To $pwshVal
        } -TestCases @(
            @{ Expr = '@{}' }
            @{ Expr = '@{ Key = "Value" }' }
            @{ Expr = '@{ Item = @(1, 2, 3) }' }
            @{ Expr = '@{ Rules = @{ MyRule = @{ Setting1 = "Hello"; Setting2 = 42 } } }' }
            @{ Expr = '@{ Rules = @{ MyRule = @{ Setting1 = 7,4,6,1; Setting2 = 42 } } }' }
            @{ Expr = '@{ Rules = @{ MyRule = @{ Setting1 = @(); Setting2 = 42 } } }' }
            @{ Expr = '@{ Rules = @{ MyRule = @{ Setting1 = @(9, 2, 1, "Hello"); Setting2 = 42 } } }' }
            @{ Expr = '@{ Rules = @{ MyRule = @{ Setting1 = @(9, @(3, 6), 1, "Hello"); Setting2 = 42 } } }' }
            @{ Expr = '@{ Rules = @{ MyRule = @{ Setting1 = @(9, @{ x = 10; y = 11 }, 1, "Hello"); Setting2 = 42 } } }' }
        )

        It "Rejects the input <Expr>" {
            param([string]$Expr)

            $exprAst = [System.Management.Automation.Language.Parser]::ParseInput($Expr, [ref]$null, [ref]$null)
            $exprAst = $exprAst.Find( { $args[0] -is [System.Management.Automation.Language.ExpressionAst] }, $true)

            $expectedError = 'InvalidDataException'
            if ($null -eq $exprAst)
            {
                $expectedError = 'ArgumentNullException'
            }

            { $gsvVal = [Microsoft.Windows.PowerShell.ScriptAnalyzer.Helper]::GetSafeValueFromExpressionAst($exprAst) } | Should -Throw -ErrorId $expectedError
        } -TestCases @(
            @{ Expr = '$var' }
            @{ Expr = '' }
            @{ Expr = '3+7' }
            @{ Expr = '- 2.5' }
            @{ Expr = '-not $true' }
            @{ Expr = '@(1, Get-Thing)' }
            @{ Expr = '@{ Key = Get-Thing }' }
            @{ Expr = '@{ Thing = $true;7 ' }
            @{ Expr = '@{ Thing = @(Asset-Thing;10) ' }
            @{ Expr = ';)' }
        )
    }

    Context "FindSettingsMode" {
        BeforeAll {
            $findSettingsMode = ($settingsTypeName -as [type]).GetMethod(
                'FindSettingsMode',
                [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Static)

            $outputObject = [System.Object]::new()
        }

        It "Should detect hashtable" {
            $settings = @{}
            $findSettingsMode.Invoke($null, @($settings, $null, [ref]$outputObject)) | Should -Be "Hashtable"
        }

        It "Should detect hashtable wrapped by a PSObject" {
            $settings = [PSObject]@{} # Force the settings hashtable to be wrapped
            $findSettingsMode.Invoke($null, @($settings, $null, [ref]$outputObject)) | Should -Be "Hashtable"
        }

        It "Should detect string" {
            $settings = ""
            $findSettingsMode.Invoke($null, @($settings, $null, [ref]$outputObject)) | Should -Be "File"
        }

        It "Should detect string wrapped by a PSObject" {
            $settings = [PSObject]"" # Force the settings string to be wrapped
            $findSettingsMode.Invoke($null, @($settings, $null, [ref]$outputObject)) | Should -Be "File"
        }
    }
}

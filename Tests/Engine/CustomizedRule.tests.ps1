$directory = Split-Path -Parent $MyInvocation.MyCommand.Path
$testRootDirectory = Split-Path -Parent $directory
Import-Module (Join-Path $testRootDirectory 'PSScriptAnalyzerTestHelper.psm1')

# we need a way to determine whether the file system is case sensitive
# using $IsLinux or $IsMacOS is not sufficient
try
{
    $module = get-module pester
    # we can use the Pester New-TestDrive function
    $d = & $module New-TestDrive -Pass
    foreach ( $f in "file1","FILE2","fIlE3" ) {
        $null = new-item -type file (Join-Path $d.root $f)
    }
    Write-Verbose -Verbose ("temp drive: " + $d.root)
    $count = @(get-childitem (Join-Path $d.root "file?")).Count
    if ( $count -eq 1 ) {
        Write-Verbose -Verbose "Case Sensitive FS"
        $script:CaseSensitiveFS = $true
    } else {
        Write-Verbose -Verbose "Case Insensitive FS"
        $script:CaseSensitiveFS = $false
    }
}
finally {
    Remove-PSDrive $d
    Remove-Item -force -recurse $d.root
}

if (-not (Test-PSEditionCoreCLR))
{
	# Force Get-Help not to prompt for interactive input to download help using Update-Help
	# By adding this registry key we turn off Get-Help interactivity logic during ScriptRule parsing
	$null,"Wow6432Node" | ForEach-Object {
		try
		{
			Set-ItemProperty -Name "DisablePromptToUpdateHelp" -Path "HKLM:\SOFTWARE\$($_)\Microsoft\PowerShell" -Value 1 -Force -ErrorAction SilentlyContinue
		}
		catch
		{
			# Ignore for cases when tests are running in non-elevated more or registry key does not exist or not accessible
		}
	}
}


$message = "this is help"
$measure = "Measure-RequiresRunAsAdministrator"

Describe "Test importing customized rules with null return results" {
    Context "Test Get-ScriptAnalyzer with customized rules" {
        It "will not terminate the engine" {
            $customizedRulePath = Get-ScriptAnalyzerRule -CustomizedRulePath $directory\samplerule\SampleRulesWithErrors.psm1 | Where-Object {$_.RuleName -eq $measure}
            $customizedRulePath.Count | Should -Be 1
        }

    }

    Context "Test Invoke-ScriptAnalyzer with customized rules" {
        It "will not terminate the engine" {
            $customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule\SampleRulesWithErrors.psm1 | Where-Object {$_.RuleName -eq $measure}
            $customizedRulePath.Count | Should -Be 0
        }
    }

}

Describe "Test importing correct customized rules" {

	if(-not (Test-PSEditionCoreCLR))
	{
		Context "Test Get-Help functionality in ScriptRule parsing logic" {
			It "ScriptRule help section must be correctly processed when Get-Help is called for the first time" {

				# Force Get-Help to prompt for interactive input to download help using Update-Help
				# By removing this registry key we force to turn on Get-Help interactivity logic during ScriptRule parsing
				$null,"Wow6432Node" | ForEach-Object {
					try
					{
						Remove-ItemProperty -Name "DisablePromptToUpdateHelp" -Path "HKLM:\SOFTWARE\$($_)\Microsoft\PowerShell" -ErrorAction Stop
					} catch {
						#Ignore for cases when tests are running in non-elevated more or registry key does not exist or not accessible
					}
				}

				$customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule\samplerule.psm1 | Where-Object {$_.Message -eq $message}
				$customizedRulePath.Count | Should -Be 1

				# Force Get-Help not to prompt for interactive input to download help using Update-Help
				# By adding this registry key we turn off Get-Help interactivity logic during ScriptRule parsing
				$null,"Wow6432Node" | ForEach-Object {
					try
					{
						Set-ItemProperty -Name "DisablePromptToUpdateHelp" -Path "HKLM:\SOFTWARE\$($_)\Microsoft\PowerShell" -Value 1 -Force -EA SilentlyContinue
					}
					catch
					{
						# Ignore for cases when tests are running in non-elevated more or registry key does not exist or not accessible
					}
				}
			}
		}
	}

    Context "Test Get-ScriptAnalyzer with customized rules" {
        It "will show the custom rule" {
            $customizedRulePath = Get-ScriptAnalyzerRule  -CustomizedRulePath $directory\samplerule\samplerule.psm1 | Where-Object {$_.RuleName -eq $measure}
            $customizedRulePath.Count | Should -Be 1
        }

		It "will show the custom rule when given a rule folder path" {
			$customizedRulePath = Get-ScriptAnalyzerRule  -CustomizedRulePath $directory\samplerule | Where-Object {$_.RuleName -eq $measure}
		    $customizedRulePath.Count | Should -Be 1
		}

        It "will show the custom rule when given a rule folder path with trailing backslash" -skip:$($IsLinux -or $IsMacOS) {
			# needs fixing for linux
            $customizedRulePath = Get-ScriptAnalyzerRule  -CustomizedRulePath $directory/samplerule/ | Where-Object {$_.RuleName -eq $measure}
            $customizedRulePath.Count | Should -Be 1
		}

		It "will show the custom rules when given a glob" {
			# needs fixing for Linux
			$expectedNumRules = 4
			if ($script:CaseSensitiveFS)
			{
				$expectedNumRules = 3
			}
			$customizedRulePath = Get-ScriptAnalyzerRule  -CustomizedRulePath $directory\samplerule\samplerule* | Where-Object {$_.RuleName -match $measure}
			$customizedRulePath.Count | Should -Be $expectedNumRules
		}

		It "will show the custom rules when given recurse switch" {
			$customizedRulePath = Get-ScriptAnalyzerRule  -RecurseCustomRulePath -CustomizedRulePath "$directory\samplerule", "$directory\samplerule\samplerule2" | Where-Object {$_.RuleName -eq $measure}
			$customizedRulePath.Count | Should -Be 5
		}

		It "will show the custom rules when given glob with recurse switch" {
			$expectedNumRules = 5
			if ($script:CaseSensitiveFS)
			{
				$expectedNumRules = 4
			}
			$customizedRulePath = Get-ScriptAnalyzerRule  -RecurseCustomRulePath -CustomizedRulePath $directory\samplerule\samplerule* | Where-Object {$_.RuleName -eq $measure}
			$customizedRulePath.Count | Should -Be $expectedNumRules
		}

		It "will show the custom rules when given glob with recurse switch" {
			$customizedRulePath = Get-ScriptAnalyzerRule  -RecurseCustomRulePath -CustomizedRulePath $directory\samplerule* | Where-Object {$_.RuleName -eq $measure}
			$customizedRulePath.Count | Should -Be 3
		}
    }

    Context "Test Invoke-ScriptAnalyzer with customized rules" {
        It "will show the custom rule in the results" {
            $customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule\samplerule.psm1 | Where-Object {$_.Message -eq $message}
            $customizedRulePath.Count | Should -Be 1
        }

		It "will show the custom rule in the results when given a rule folder path" {
            $customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule | Where-Object {$_.Message -eq $message}
            $customizedRulePath.Count | Should -Be 1
        }

		It "will set ScriptName property to the target file name" {
			$violations = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule
			$violations[0].ScriptName | Should -Be 'TestScript.ps1'
		}

		It "will set ScriptPath property to the target file path" {
			$violations = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule
			$expectedScriptPath = Join-Path $directory 'TestScript.ps1'
			$violations[0].ScriptPath | Should -Be $expectedScriptPath
		}

        It "will set SuggestedCorrections" {
            $violations = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule
            $expectedScriptPath = Join-Path $directory 'TestScript.ps1'
            $violations[0].SuggestedCorrections | Should -Not -BeNullOrEmpty
            $violations[0].SuggestedCorrections.StartLineNumber   | Should -Be 1
            $violations[0].SuggestedCorrections.EndLineNumber     | Should -Be 2
            $violations[0].SuggestedCorrections.StartColumnNumber | Should -Be 3
            $violations[0].SuggestedCorrections.EndColumnNumber   | Should -Be 4
            $violations[0].SuggestedCorrections.Text   | Should -Be 'text'
            $violations[0].SuggestedCorrections.File   | Should -Be 'filePath'
            $violations[0].SuggestedCorrections.Description   | Should -Be 'description'
		}

        It "can suppress custom rule" {
			$script = "[Diagnostics.CodeAnalysis.SuppressMessageAttribute('samplerule\$measure','')]Param()"
			$testScriptPath = "TestDrive:\SuppressedCustomRule.ps1"
			Set-Content -Path $testScriptPath -Value $script

            $customizedRulePath = Invoke-ScriptAnalyzer -Path $testScriptPath -CustomizedRulePath $directory\samplerule\samplerule.psm1 |
				Where-Object { $_.Message -eq $message }

            $customizedRulePath.Count | Should -Be 0
		}

        It "can suppress custom rule with rule name expression '<RuleNameExpression>'" -TestCases @(
            @{RuleNameExpression = '$MyInvocation.MyCommand.Name'; RuleName = 'WarningAboutDoSomething' }
            @{RuleNameExpression = '$MyInvocation.InvocationName'; RuleName = 'MyCustomRule\WarningAboutDoSomething' }
            @{RuleNameExpression = "'MyRuleName'"; RuleName = 'MyRuleName' }
        ) {
            Param($RuleNameExpression, $RuleName)

            $script = @"
			[Diagnostics.CodeAnalysis.SuppressMessageAttribute('$RuleName', '')]
			Param()
			Invoke-Something
"@
            $customRuleContent = @'
			function WarningAboutDoSomething {
				param (
					[System.Management.Automation.Language.CommandAst]$ast
				)

				if ($ast.GetCommandName() -eq 'Invoke-Something') {
					New-Object -Typename 'Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic.DiagnosticRecord' `
								-ArgumentList 'This is help',$ast.Extent,REPLACE_WITH_RULE_NAME_EXPRESSION,Warning,$ast.Extent.File,$null,$null
				}
			}
'@
            $customRuleContent = $customRuleContent.Replace('REPLACE_WITH_RULE_NAME_EXPRESSION', $RuleNameExpression)
            $testScriptPath = "TestDrive:\SuppressedCustomRule.ps1"
            Set-Content -Path $testScriptPath -Value $script
            $customRuleScriptPath = Join-Path $TestDrive 'MyCustomRule.psm1'
			Set-Content -Path $customRuleScriptPath -Value $customRuleContent
			$violationsWithoutSuppresion = Invoke-ScriptAnalyzer -ScriptDefinition 'Invoke-Something' -CustomRulePath $customRuleScriptPath
			$violationsWithoutSuppresion.Count | Should -Be 1
            $violations = Invoke-ScriptAnalyzer -Path $testScriptPath -CustomRulePath $customRuleScriptPath
			$violations.Count | Should -Be 0
			$violationsWithIncludeDefaultRules = Invoke-ScriptAnalyzer -Path $testScriptPath -CustomRulePath $customRuleScriptPath -IncludeDefaultRules
            $violationsWithIncludeDefaultRules.Count | Should -Be 0
        }

        It "will set RuleSuppressionID" {
            $violations = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule
            $violations[0].RuleSuppressionID   | Should -Be "MyRuleSuppressionID"
        }

        if (!$testingLibraryUsage)
		{
            It "will show the custom rule in the results when given a rule folder path with trailing backslash" {
				# needs fixing for Linux
				if (!$IsLinux -and !$IsMacOS)
				{
					$customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule\ | Where-Object {$_.Message -eq $message}
					$customizedRulePath.Count | Should -Be 1
				}
		    }

		    It "will show the custom rules when given a glob" {
			    $customizedRulePath = Invoke-ScriptAnalyzer  $directory\TestScript.ps1 -CustomizedRulePath $directory\samplerule\samplerule* | Where-Object {$_.Message -eq $message}
			    $customizedRulePath.Count | Should -Be 3
		    }

		    It "will show the custom rules when given recurse switch" {
			    $customizedRulePath = Invoke-ScriptAnalyzer  $directory\TestScript.ps1 -RecurseCustomRulePath -CustomizedRulePath $directory\samplerule | Where-Object {$_.Message -eq $message}
			    $customizedRulePath.Count | Should -Be 3
		    }

		    It "will show the custom rules when given glob with recurse switch" {
			    $customizedRulePath = Invoke-ScriptAnalyzer  $directory\TestScript.ps1 -RecurseCustomRulePath -CustomizedRulePath $directory\samplerule\samplerule* | Where-Object {$_.Message -eq $message}
			    $customizedRulePath.Count | Should -Be 4
		    }

		    It "will show the custom rules when given glob with recurse switch" {
			    $customizedRulePath = Invoke-ScriptAnalyzer  $directory\TestScript.ps1 -RecurseCustomRulePath -CustomizedRulePath $directory\samplerule* | Where-Object {$_.Message -eq $message}
			    $customizedRulePath.Count | Should -Be 3
		    }

            It "Using IncludeDefaultRules Switch with CustomRulePath" {
                $customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomRulePath $directory\samplerule\samplerule.psm1 -IncludeDefaultRules
                $customizedRulePath.Count | Should -Be 2
            }

            It "Using IncludeDefaultRules Switch without CustomRulePath" {
                $customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -IncludeDefaultRules
                $customizedRulePath.Count | Should -Be 1
            }

            It "Not Using IncludeDefaultRules Switch and without CustomRulePath" {
                $customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1
                $customizedRulePath.Count | Should -Be 1
            }

			It "loads custom rules that contain version in their path" -Skip:($PSVersionTable.PSVersion -lt [Version]'5.0.0') {
			$customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomRulePath $directory\VersionedSampleRule\SampleRuleWithVersion
			$customizedRulePath.Count | Should -Be 1

			$customizedRulePath = Get-ScriptAnalyzerRule -CustomRulePath $directory\VersionedSampleRule\SampleRuleWithVersion
			$customizedRulePath.Count | Should -Be 1
			}

			It "loads custom rules that contain version in their path with the RecurseCustomRule switch" -Skip:($PSVersionTable.PSVersion -lt [Version]'5.0.0') {
			$customizedRulePath = Invoke-ScriptAnalyzer $directory\TestScript.ps1 -CustomRulePath $directory\VersionedSampleRule -RecurseCustomRulePath
			$customizedRulePath.Count | Should -Be 1

			$customizedRulePath = Get-ScriptAnalyzerRule -CustomRulePath $directory\VersionedSampleRule -RecurseCustomRulePath
			$customizedRulePath.Count | Should -Be 1
		}
        }

    }
}


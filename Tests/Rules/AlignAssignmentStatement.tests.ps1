# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

BeforeAll {
    $testRootDirectory = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $testRootDirectory "PSScriptAnalyzerTestHelper.psm1")

    $ruleConfiguration = @{
        Enable         = $true
        CheckHashtable = $true
    }

    $settings = @{
        IncludeRules = @("PSAlignAssignmentStatement")
        Rules        = @{
            PSAlignAssignmentStatement = $ruleConfiguration
        }
    }
}

Describe "AlignAssignmentStatement" {
    Context "When assignment statements are in hashtable" {
        It "Should find violation when assignment statements are not aligned (whitespace needs to be added)" {
            $def = @'
$hashtable = @{
    property1 = "value"
    anotherProperty = "another value"
}
'@

            # Expected output after correction should be the following
            # $hashtable = @{
            #     property1       = "value"
            #     anotherProperty = "another value"
            # }

            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
            Test-CorrectionExtentFromContent $def $violations 1 ' ' '       '
        }

        It "Should find violation when assignment statements are not aligned (whitespace needs to be removed)" {
            $def = @'
$hashtable = @{
    property1              = "value"
    anotherProperty = "another value"
}
'@

            # Expected output should be the following
            # $hashtable = @{
            #     property1       = "value"
            #     anotherProperty = "another value"
            # }

            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings
            $violations.Count | Should -Be 1
            Test-CorrectionExtentFromContent $def $violations 1 '              ' '       '
        }

        It "Should not crash if property name reaches further to the right than the longest property name (regression test for issue 1067)" {
            $def = @'
$hashtable = @{ property1 = "value"
    anotherProperty       = "another value"
}
'@

            $violations = Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings -ErrorAction Stop | Get-Count | Should -Be 0
        }

        It "Should ignore if a hashtable is empty" {
            $def = @'
$x = @{ }
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Get-Count | Should -Be 0

        }

        It "Should ignore if a hashtable has a single key-value pair on a single line" {
            $def = @'
$x = @{ 'key'="value" }
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Get-Count | Should -Be 0

        }

        It "Should ignore if a hashtable has a single key-value pair across multiple lines" {
            $def = @'
$x = @{ 
    'key'="value" 
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Get-Count | Should -Be 0

        }

        It "Should ignore if a hashtable has multiple key-value pairs on a single line" {
            $def = @'
$x = @{ 'key'="value"; 'key2'="value2"; 'key3WithLongerName'="value3" }
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Get-Count | Should -Be 0

        }

    }

    Context "When assignment statements are in DSC Configuration" {
        It "Should find violations when assignment statements are not aligned" -skip:($IsLinux -or $IsMacOS) {
            $def = @'
Configuration MyDscConfiguration {

    param(
        [string[]]$ComputerName="localhost"
    )
    Node $ComputerName {
        WindowsFeature MyFeatureInstance {
            Ensure = "Present"
            Name =  "RSAT"
        }
        WindowsFeature My2ndFeatureInstance {
            Ensure = "Present"
            Name = "Bitlocker"
        }
    }
}
'@
            Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings | Get-Count | Should -Be 2
        }
    }

    if ($PSVersionTable.PSVersion.Major -ge 5) {
        Context "When assignment statements are in DSC Configuration that has parse errors" {
            It "Should find violations when assignment statements are not aligned" -skip:($IsLinux -or $IsMacOS) {
                $def = @'
Configuration Sample_ChangeDescriptionAndPermissions
{
    Import-DscResource -Module NonExistentModule
    # A Configuration block can have zero or more Node blocks
    Node $NodeName
    {
        # Next, specify one or more resource blocks

        NonExistentModule MySMBShare
        {
            Ensure  = "Present"
            Name   = "MyShare"
            Path    = "C:\Demo\Temp"
            ReadAccess  = "author"
            FullAccess        = "some other author"
            Description = "This is an updated description for this share"
        }
    }
}
'@
                # This invocation will throw parse error caused by "Undefined DSC resource" because
                # NonExistentModule is not really avaiable to load. Therefore we set erroraction to
                # SilentlyContinue
                Invoke-ScriptAnalyzer -ScriptDefinition $def -Settings $settings -ErrorAction SilentlyContinue |
                    Where-Object { $_.Severity -ne "ParseError" } |
                    Get-Count |
                    Should -Be 4
            }
        }
    }
}

# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

Describe "UseCorrectCasing" {
    It "corrects case of simple cmdlet" {
        Invoke-Formatter 'get-childitem' | Should -BeExactly 'Get-ChildItem'
    }

    It "corrects case of fully qualified cmdlet" {
        Invoke-Formatter 'Microsoft.PowerShell.management\get-childitem' | Should -BeExactly 'Microsoft.PowerShell.Management\Get-ChildItem'
    }

    It "corrects case of of cmdlet inside interpolated string" {
        Invoke-Formatter '"$(get-childitem)"' | Should -BeExactly '"$(Get-ChildItem)"'
    }

    It "Corrects alias correctly" {
        Invoke-Formatter 'Gci' | Should -BeExactly 'gci'
        Invoke-Formatter '?' | Should -BeExactly '?'
    }

    It "Does not corrects applications on the PATH" -Skip:($IsLinux -or $IsMacOS) {
        Invoke-Formatter 'Git' | Should -BeExactly 'Git'
        Invoke-Formatter 'SSH' | Should -BeExactly 'SSH'
    }

    It "Preserves extension of applications on Windows" -Skip:($IsLinux -or $IsMacOS) {
        Invoke-Formatter 'cmd.exe' | Should -BeExactly 'cmd.exe'
        Invoke-Formatter 'more.com' | Should -BeExactly 'more.com'
    }

    It "Preserves full application path" {
        if ($IsLinux -or $IsMacOS) {
            $applicationPath = '. /bin/ls'
        }
        else {
            $applicationPath = "${env:WINDIR}\System32\cmd.exe"
        }
        Invoke-Formatter ". $applicationPath" | Should -BeExactly ". $applicationPath"
    }

    # TODO: Can we make this work?
    # There is a limitation in the Helper's CommandCache: it doesn't see commands that are (only temporarily) defined in the current scope
    It "Corrects case of script function" -Skip {
        function global:Invoke-DummyFunction { }
        Invoke-Formatter 'invoke-dummyFunction' | Should -BeExactly 'Invoke-DummyFunction'
    }

    It "Preserves script path" {
        $path = Join-Path $TestDrive "$([guid]::NewGuid()).ps1"
        New-Item -ItemType File -Path $path
        $scriptDefinition = ". $path"
        Invoke-Formatter $scriptDefinition | Should -BeExactly $scriptDefinition
    }

    It "Preserves UNC script path" -Skip:($IsLinux -or $IsMacOS) {
        $uncPath = [System.IO.Path]::Combine("\\$(HOSTNAME.EXE)\C$\", $TestDrive, "$([guid]::NewGuid()).ps1")
        New-Item -ItemType File -Path $uncPath
        $scriptDefinition = ". $uncPath"
        Invoke-Formatter $scriptDefinition | Should -BeExactly $scriptDefinition
    }

    It "Corrects parameter casing" {
        # Without messing up the spacing or use of semicolons
        Invoke-Formatter 'Get-ChildItem -literalpath $parameterValue' |
            Should -BeExactly 'Get-ChildItem -LiteralPath $parameterValue'
        Invoke-Formatter 'Get-ChildItem -literalpath:$parameterValue' |
            Should -BeExactly 'Get-ChildItem -LiteralPath:$parameterValue'
        Invoke-Formatter 'Get-ChildItem -literalpath: $parameterValue' |
            Should -BeExactly 'Get-ChildItem -LiteralPath: $parameterValue'
    }

    It "Should not throw when using parameter name that does not exist" {
        Invoke-Formatter 'Get-Process -NonExistingParameterName' -ErrorAction Stop
    }

    It "Does not throw when correcting certain cmdlets (issue 1516)" {
        $scriptDefinition = 'Get-Content;Test-Path;Get-ChildItem;Get-Content;Test-Path;Get-ChildItem'
        $settings = @{ 'Rules' = @{ 'PSUseCorrectCasing' = @{ 'Enable' = $true; CheckCommands = $true; CheckKeywords = $true; CheckOperators = $true } } }
        {
            1..100 |
            ForEach-Object { $null = Invoke-ScriptAnalyzer -ScriptDefinition $scriptDefinition -Settings $settings -ErrorAction Stop }
        } |
        Should -Not -Throw
    }

    It "Corrects uppercase operators" {
        Invoke-Formatter '$ENV:PATH -SPLIT ";"' |
            Should -BeExactly '$ENV:PATH -split ";"'
    }

    It "Corrects mixed case operators" {
        Invoke-Formatter '$ENV:PATH -Split ";" -Join ":"' |
            Should -BeExactly '$ENV:PATH -split ";" -join ":"'
    }

    It "Corrects unary operators" {
        Invoke-Formatter '-Split "Hello World"' |
            Should -BeExactly '-split "Hello World"'
    }
    It "Does not break PlusPlus or MinusMinus" {
        Invoke-Formatter '$A++; $B--' |
            Should -BeExactly '$A++; $B--'
    }

    Context "Inconsistent Keywords" {
        It "Corrects keyword case" {
            Invoke-Formatter 'ForEach ($x IN $y) { $x }' |
                Should -BeExactly 'foreach ($x in $y) { $x }'
        }
    }
}

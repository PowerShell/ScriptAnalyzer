# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

$ErrorActionPreference = 'Stop'

function Install-Pester {
    $pester = Get-Module Pester -ListAvailable | Where-Object { $_.Version -gt [version]'5.1' }
    if ($null -eq $pester) {
        if ($null -eq (Get-Module -ListAvailable PowershellGet)) {
            # WMF 4 image build
            Write-Verbose -Verbose "Installing Pester via nuget"
            nuget install Pester -source https://www.powershellgallery.com/api/v2 -outputDirectory "$env:ProgramFiles\WindowsPowerShell\Modules\." -ExcludeVersion
        }
        else {
            # Visual Studio 2017 build (has already Pester v3, therefore a different installation mechanism is needed to make it also use the new version 4)
            Write-Verbose -Verbose "Installing Pester via Install-Module"
            Install-Module -Name Pester -Force -SkipPublisherCheck -Scope CurrentUser -Repository PSGallery
        }
        Write-Verbose -Verbose 'Installed Pester'
    }
}

# Implements the AppVeyor 'install' step and installs the required versions of Pester, platyPS and the .Net Core SDK if needed.
function Invoke-AppVeyorInstall {
    param(
        # For the multi-stage build in Azure DevOps, Pester is not needed for bootstrapping the build environment
        [switch] $SkipPesterInstallation
    )

    $installPowerShellModulesjobs = @()
    if (-not $SkipPesterInstallation.IsPresent) { $installPowerShellModulesjobs += Start-Job ${Function:Install-Pester} }

    $installPowerShellModulesjobs += Start-Job {
        if ($null -eq (Get-Module -ListAvailable PowershellGet)) {
            # WMF 4 image build
            Write-Verbose -Verbose "Installing platyPS via nuget"
            nuget install platyPS -source https://www.powershellgallery.com/api/v2 -outputDirectory "$Env:ProgramFiles\WindowsPowerShell\Modules\." -ExcludeVersion
        }
        else {
            Write-Verbose -Verbose "Installing platyPS via Install-Module"
            Install-Module -Name platyPS -Force -Scope CurrentUser -Repository PSGallery
        }
        Write-Verbose -Verbose 'Installed platyPS'
    }

    # Do not use 'build.ps1 -bootstrap' option for bootstraping the .Net SDK as it does not work well in CI with the AppVeyor Ubuntu image
    Write-Verbose -Verbose "Installing required .Net CORE SDK"
    # the legacy WMF4 image only has the old preview SDKs of dotnet
    $globalDotJson = Get-Content (Join-Path $PSScriptRoot '..\global.json') -Raw | ConvertFrom-Json
    $requiredDotNetCoreSDKVersion = $globalDotJson.sdk.version
    if ($PSVersionTable.PSVersion.Major -gt 4) {
        $requiredDotNetCoreSDKVersionPresent = (dotnet --list-sdks) -match $requiredDotNetCoreSDKVersion
    }
    else {
        # WMF 4 image has old SDK that does not have --list-sdks parameter
        $requiredDotNetCoreSDKVersionPresent = (dotnet --version).StartsWith($requiredDotNetCoreSDKVersion)
    }
    if (-not $requiredDotNetCoreSDKVersionPresent) {
        Write-Verbose -Verbose "Installing required .Net CORE SDK $requiredDotNetCoreSDKVersion"
        $originalSecurityProtocol = [Net.ServicePointManager]::SecurityProtocol
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            if ($IsLinux -or $isMacOS) {
                Invoke-WebRequest 'https://dot.net/v1/dotnet-install.sh' -OutFile dotnet-install.sh
                bash dotnet-install.sh --version $requiredDotNetCoreSDKVersion
                [System.Environment]::SetEnvironmentVariable('PATH', "/home/appveyor/.dotnet$([System.IO.Path]::PathSeparator)$PATH")
            }
            else {
                Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile dotnet-install.ps1
                .\dotnet-install.ps1 -Version $requiredDotNetCoreSDKVersion
            }
        }
        finally {
            [Net.ServicePointManager]::SecurityProtocol = $originalSecurityProtocol
            Remove-Item .\dotnet-install.*
        }
        Write-Verbose -Verbose 'Installed required .Net CORE SDK'
    }

    Wait-Job $installPowerShellModulesjobs | Receive-Job
    $installPowerShellModulesjobs | ForEach-Object {
        if ($_.State -eq 'Failed') {
            throw 'Installing PowerShell modules failed, see job logs above'
        }
    }
}

# Implements AppVeyor 'test_script' step
function Invoke-AppveyorTest {
    Param(
        [Parameter(Mandatory)]
        [ValidateScript( {Test-Path $_})]
        $CheckoutPath
    )

    Install-Pester

    # enforce the language to utf-8 to avoid issues
    $env:LANG = "en_US.UTF-8"
    Write-Verbose -Verbose ("Running tests on PowerShell version " + $PSVersionTable.PSVersion)
    Write-Verbose -Verbose "Language set to '${env:LANG}'"

    # set up env:PSModulePath to the build location, don't copy it to the "normal place"
    $analyzerVersion = ([xml](Get-Content "${CheckoutPath}\Engine\Engine.csproj")).SelectSingleNode(".//VersionPrefix")."#text".Trim()
    $majorVersion = ([System.Version]$analyzerVersion).Major
    $psMajorVersion = $PSVersionTable.PSVersion.Major

    if ( $psMajorVersion -lt 5 ) {
        $versionModuleDir = "${CheckoutPath}\out\PSScriptAnalyzer\${analyzerVersion}"
        $renameTarget = "${CheckoutPath}\out\PSScriptAnalyzer\PSScriptAnalyzer"
        Rename-Item "${versionModuleDir}" "${renameTarget}"
        $moduleDir = "${CheckoutPath}\out\PSScriptAnalyzer"
    }
    else{
        $moduleDir = "${CheckoutPath}\out"
    }

    $env:PSModulePath = "${moduleDir}","${env:PSModulePath}" -join [System.IO.Path]::PathSeparator
    Write-Verbose -Verbose "module path: ${env:PSModulePath}"

    # Set up testing assets
    [string[]] $testScripts = @(
        Join-Path $CheckoutPath 'Tests\Engine'
        Join-Path $CheckoutPath 'Tests\Rules'
        Join-Path $CheckoutPath 'Tests\Documentation'
        Join-Path $CheckoutPath 'PSCompatibilityCollector\Tests'
    )

    # Change culture to Turkish to test that PSSA works well with different locales
    [System.Threading.Thread]::CurrentThread.CurrentCulture = [cultureinfo]::CreateSpecificCulture('tr-TR')
    [System.Threading.Thread]::CurrentThread.CurrentUICulture = [cultureinfo]::CreateSpecificCulture('tr-TR')

    # Run all tests
    Import-Module PSScriptAnalyzer
    Import-Module Pester
    $configuration = [PesterConfiguration]::Default
    $configuration.CodeCoverage.Enabled = $false
    $configuration.Output.Verbosity = 'Normal'
    $configuration.Run.Exit = $true
    $configuration.Run.PassThru = $true
    $configuration.Run.Path = $testScripts
    $configuration.TestResult.Enabled = $true
    Invoke-Pester -Configuration $configuration
}

# Implements AppVeyor 'on_finish' step
function Invoke-AppveyorFinish {
    $uploadUrl = "https://ci.appveyor.com/api/testresults/nunit/${env:APPVEYOR_JOB_ID}"
    $testResultsPath = Join-Path $pwd TestResults.xml # default when using the -CI switch in Invoke-Pester
    Write-Verbose -Verbose "Uploading test results '$testResultsPath' to '${uploadUrl}'"
    $null = (New-Object 'System.Net.WebClient').UploadFile("$uploadUrl" , $testResultsPath)

    $stagingDirectory = (Resolve-Path ..).Path
    $zipFile = Join-Path $stagingDirectory "$(Split-Path $pwd -Leaf).zip"
    Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
    [System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $pwd 'out'), $zipFile)
    @(
        # add test results as an artifact
        (Get-ChildItem testResults.xml)
        # You can add other artifacts here
        (Get-ChildItem $zipFile)
    ) | ForEach-Object { Push-AppveyorArtifact $_.FullName }
}

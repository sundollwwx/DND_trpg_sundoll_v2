[CmdletBinding()]
param(
    [string]$UnityPath = "",
    [string]$ProjectPath = "",
    [string]$EvidenceRoot = "",
    [int]$ExpectedEditModeCount = 97,
    [int]$ExpectedPlayModeCount = 16,
    [switch]$SkipTests,
    [switch]$SkipBuild,
    [switch]$AllowBuildOutputOverwrite,
    [string]$PackagePath = "",
    [string]$ExpectedCanonicalHash = "",
    [string]$ActualCanonicalHash = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return ""
    }

    return [System.IO.Path]::GetFullPath($PathValue)
}

function Quote-ProcessArgument([string]$Value) {
    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-UnityStep(
    [string]$StepName,
    [string[]]$Arguments,
    [string]$LogPath
) {
    $quotedArguments = @($Arguments | ForEach-Object { Quote-ProcessArgument $_ })
    Write-Host "[$StepName] Unity $($Arguments -join ' ')"
    Write-Host "[$StepName] Log: $LogPath"

    $process = Start-Process `
        -FilePath $script:UnityPath `
        -ArgumentList $quotedArguments `
        -Wait `
        -PassThru

    if ($process.ExitCode -ne 0) {
        throw "$StepName failed with Unity exit code $($process.ExitCode). See $LogPath"
    }

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "$StepName exited successfully but did not create log $LogPath"
    }

    $logText = Get-Content -LiteralPath $LogPath -Raw
    if ($logText -match 'Unsupported protocol version|Licensing initialization failed|ObjectDisposedException|The re-connection attempt was UN-successful') {
        throw "$StepName log contains a Unity licensing or connection failure. See $LogPath"
    }
    if ($logText -match 'error CS\d+|Compilation failed') {
        throw "$StepName log contains a C# compilation failure. See $LogPath"
    }

    return $process.ExitCode
}

function Get-XmlAttributeInt([System.Xml.XmlElement]$Element, [string]$Name) {
    $value = $Element.GetAttribute($Name)
    $parsed = 0
    if (-not [int]::TryParse($value, [ref]$parsed)) {
        throw "Test XML attribute '$Name' is missing or not an integer."
    }

    return $parsed
}

function Assert-TestResult(
    [string]$StepName,
    [string]$ResultPath,
    [int]$MinimumTestCount
) {
    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        throw "$StepName did not create test XML $ResultPath"
    }

    $document = New-Object System.Xml.XmlDocument
    $document.Load($ResultPath)
    $testRun = $document.DocumentElement
    if ($null -eq $testRun -or $testRun.Name -ne "test-run") {
        throw "$StepName produced XML without a NUnit test-run root: $ResultPath"
    }

    $result = $testRun.GetAttribute("result")
    $total = Get-XmlAttributeInt $testRun "total"
    $testCaseCount = Get-XmlAttributeInt $testRun "testcasecount"
    $passed = Get-XmlAttributeInt $testRun "passed"
    $failed = Get-XmlAttributeInt $testRun "failed"
    $skipped = Get-XmlAttributeInt $testRun "skipped"
    $inconclusive = Get-XmlAttributeInt $testRun "inconclusive"

    if ($result -ne "Passed" -or
        $failed -ne 0 -or
        $skipped -ne 0 -or
        $inconclusive -ne 0 -or
        $passed -ne $total -or
        $total -ne $testCaseCount -or
        $testCaseCount -lt $MinimumTestCount) {
        throw "$StepName failed validation: result=$result, testcasecount=$testCaseCount, total=$total, passed=$passed, failed=$failed, skipped=$skipped, inconclusive=$inconclusive. See $ResultPath"
    }

    Write-Host "[$StepName] Passed: $passed/$testCaseCount; failed=$failed; skipped=$skipped; inconclusive=$inconclusive"
}

function Add-Step(
    [string]$Name,
    [string]$Status,
    [string]$Details,
    [string]$Evidence = ""
) {
    $step = [ordered]@{
        name = $Name
        status = $Status
        details = $Details
    }
    if (-not [string]::IsNullOrWhiteSpace($Evidence)) {
        $step.evidence = $Evidence
    }
    $script:Steps.Add([pscustomobject]$step)
}

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDirectory

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = Join-Path $repoRoot "SundollWorld"
}
$ProjectPath = Resolve-FullPath $ProjectPath

if ([string]::IsNullOrWhiteSpace($UnityPath)) {
    $UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.22f1\Editor\Unity.exe"
}
$UnityPath = Resolve-FullPath $UnityPath

if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $ProjectPath "Docs\Evidence\Windows"
}
$EvidenceRoot = Resolve-FullPath $EvidenceRoot

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runRoot = Join-Path $EvidenceRoot "M7-Windows-$timestamp"
$logsRoot = Join-Path $runRoot "Logs"
$resultsRoot = Join-Path $runRoot "TestResults"
$buildOutputPath = Join-Path $ProjectPath "..\Builds\SundollWorld-v03-M7-Windows-x64\SundollWorld.exe"
$summaryPath = Join-Path $runRoot "validation-summary.json"
$script:Steps = New-Object System.Collections.Generic.List[object]
$startedAt = Get-Date
$overallStatus = "Passed"
$fatalError = ""
$windowsCaption = "Unknown"
$windowsVersion = "Unknown"
$windowsBuild = "Unknown"

try {
    $windowsInfo = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
    $windowsCaption = [string]$windowsInfo.Caption
    $windowsVersion = [string]$windowsInfo.Version
    $windowsBuild = [string]$windowsInfo.BuildNumber
}
catch {
    $windowsCaption = "Unavailable: $($_.Exception.Message)"
}

New-Item -ItemType Directory -Path $logsRoot, $resultsRoot -Force | Out-Null

try {
    if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
        throw "Unity Editor not found: $UnityPath"
    }
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) {
        throw "Unity project not found: $ProjectPath"
    }

    $projectVersionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path -LiteralPath $projectVersionFile -PathType Leaf)) {
        throw "Unity ProjectVersion.txt not found: $projectVersionFile"
    }
    $projectVersionText = Get-Content -LiteralPath $projectVersionFile -Raw
    if ($projectVersionText -notmatch 'm_EditorVersion:\s*6000\.3\.22f1(?:\s|$)') {
        throw "Project is not pinned to Unity 6000.3.22f1: $projectVersionFile"
    }
    Add-Step "Environment" "Passed" "Unity 6000.3.22f1 and project paths were found." "$projectVersionFile"

    $editResultPath = Join-Path $resultsRoot "TestResults_EditMode.xml"
    $editLogPath = Join-Path $logsRoot "EditMode.log"
    $playResultPath = Join-Path $resultsRoot "TestResults_PlayMode.xml"
    $playLogPath = Join-Path $logsRoot "PlayMode.log"

    if ($SkipTests) {
        Add-Step "EditMode" "NotRun" "Skipped by -SkipTests." "$editResultPath"
        Add-Step "PlayMode" "NotRun" "Skipped by -SkipTests." "$playResultPath"
    }
    else {
        $editArguments = @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $ProjectPath,
            "-runTests", "-testPlatform", "EditMode",
            "-testResults", $editResultPath,
            "-logFile", $editLogPath
        )
        Invoke-UnityStep "EditMode" $editArguments $editLogPath | Out-Null
        Assert-TestResult "EditMode" $editResultPath $ExpectedEditModeCount
        Add-Step "EditMode" "Passed" "At least $ExpectedEditModeCount tests passed with no failed, skipped, or inconclusive tests." $editResultPath

        $playArguments = @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $ProjectPath,
            "-runTests", "-testPlatform", "PlayMode",
            "-testResults", $playResultPath,
            "-logFile", $playLogPath
        )
        Invoke-UnityStep "PlayMode" $playArguments $playLogPath | Out-Null
        Assert-TestResult "PlayMode" $playResultPath $ExpectedPlayModeCount
        Add-Step "PlayMode" "Passed" "At least $ExpectedPlayModeCount tests passed with no failed, skipped, or inconclusive tests." $playResultPath
    }

    if ($SkipBuild) {
        Add-Step "Windows IL2CPP Build" "NotRun" "Skipped by -SkipBuild." $buildOutputPath
    }
    else {
        $buildOutputDirectory = Split-Path -Parent $buildOutputPath
        if ((Test-Path -LiteralPath $buildOutputDirectory -PathType Container) -and -not $AllowBuildOutputOverwrite) {
            throw "Build output directory already exists: $buildOutputDirectory. Use -AllowBuildOutputOverwrite only when replacing that artifact is intentional."
        }

        $buildLogPath = Join-Path $logsRoot "Windows-IL2CPP-Build.log"
        $buildArguments = @(
            "-batchmode", "-nographics", "-quit",
            "-projectPath", $ProjectPath,
            "-executeMethod", "Sundoll.EditorTools.M7BuildValidation.BuildWindows64Il2Cpp",
            "-logFile", $buildLogPath
        )
        Invoke-UnityStep "Windows IL2CPP Build" $buildArguments $buildLogPath | Out-Null
        if (-not (Test-Path -LiteralPath $buildOutputPath -PathType Leaf)) {
            throw "Unity exited successfully but Windows executable is missing: $buildOutputPath"
        }
        $buildDataPath = Join-Path (Split-Path -Parent $buildOutputPath) "SundollWorld_Data"
        if (-not (Test-Path -LiteralPath $buildDataPath -PathType Container)) {
            throw "Windows executable exists but data folder is missing: $buildDataPath"
        }
        $buildLogText = Get-Content -LiteralPath $buildLogPath -Raw
        if ($buildLogText -notmatch 'M7 Windows x64 IL2CPP build result:\s*Succeeded') {
            throw "Build log does not contain the expected successful IL2CPP marker: $buildLogPath"
        }
        $sizeBytes = (Get-Item -LiteralPath $buildOutputPath).Length
        Add-Step "Windows IL2CPP Build" "Passed" "Succeeded; executable size is $sizeBytes bytes." $buildOutputPath
    }

    if ([string]::IsNullOrWhiteSpace($PackagePath) -and
        [string]::IsNullOrWhiteSpace($ExpectedCanonicalHash) -and
        [string]::IsNullOrWhiteSpace($ActualCanonicalHash)) {
        Add-Step "Canonical Hash" "NotRun" "No package or hash values were supplied; no hash was invented." ""
    }
    else {
        if ([string]::IsNullOrWhiteSpace($PackagePath) -or
            [string]::IsNullOrWhiteSpace($ExpectedCanonicalHash) -or
            [string]::IsNullOrWhiteSpace($ActualCanonicalHash)) {
            throw "Canonical Hash verification requires -PackagePath, -ExpectedCanonicalHash, and -ActualCanonicalHash together."
        }
        $resolvedPackagePath = Resolve-FullPath $PackagePath
        if (-not (Test-Path -LiteralPath $resolvedPackagePath -PathType Leaf)) {
            throw "Package file not found: $resolvedPackagePath"
        }
        $expectedHash = $ExpectedCanonicalHash.Trim().ToLowerInvariant()
        $actualHash = $ActualCanonicalHash.Trim().ToLowerInvariant()
        if ($expectedHash -ne $actualHash) {
            throw "Canonical Hash mismatch for $resolvedPackagePath. Expected=$ExpectedCanonicalHash Actual=$ActualCanonicalHash"
        }
        Add-Step "Canonical Hash" "Passed" "Expected and actual values match for the supplied package." $resolvedPackagePath
    }
}
catch {
    $overallStatus = "Failed"
    $fatalError = $_.Exception.Message
    Add-Step "Overall" "Failed" $fatalError
    Write-Host "ERROR: $fatalError" -ForegroundColor Red
}
finally {
    if ($overallStatus -eq "Passed") {
        if ($SkipTests -and $SkipBuild) {
            $overallStatus = "NotValidated"
        }
        elseif ($SkipTests -or $SkipBuild) {
            $overallStatus = "Partial"
        }
    }
    $finishedAt = Get-Date
    $summary = [ordered]@{
        status = $overallStatus
        startedAt = $startedAt.ToString("o")
        finishedAt = $finishedAt.ToString("o")
        durationSeconds = [math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        unityPath = $UnityPath
        unityVersion = "6000.3.22f1"
        powershellVersion = $PSVersionTable.PSVersion.ToString()
        windowsCaption = $windowsCaption
        windowsVersion = $windowsVersion
        windowsBuild = $windowsBuild
        projectPath = $ProjectPath
        evidenceRoot = $runRoot
        expectedEditModeCount = $ExpectedEditModeCount
        expectedPlayModeCount = $ExpectedPlayModeCount
        skipTests = [bool]$SkipTests
        skipBuild = [bool]$SkipBuild
        buildOutputPath = $buildOutputPath
        error = $fatalError
        steps = @($script:Steps)
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Host "Validation summary: $summaryPath"
}

if ($overallStatus -ne "Passed") {
    exit 1
}

exit 0

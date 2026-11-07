# Japanese minimal UI test run. Never operates CMO and never writes the installed game:
# the "host" step patches a synthetic game directory whose Command.exe is the fixture.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test_ja.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test_ja.ps1 -Candidate build/ja-candidate
#
# Writes everything under a fresh build/ja-tests/<timestamp>/ (or -OutputDir, which must
# not exist) and records step results plus SHA256 of every artifact in test-report.json.
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$Candidate,
    [switch]$SkipChineseControl,
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDir) { $OutputDir = Join-Path $repo ('build\ja-tests\' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
if (Test-Path -LiteralPath $OutputDir) { throw "Output directory already exists: $OutputDir" }
New-Item -ItemType Directory -Path $OutputDir | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$steps = New-Object System.Collections.ArrayList
$utf8 = New-Object Text.UTF8Encoding($false)

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Invoke-Step([string]$Name, [string]$File, [string[]]$Arguments, [string]$WorkDir = $repo) {
    $log = Join-Path $OutputDir ($Name + '.log')
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    Push-Location $WorkDir
    try {
        $global:LASTEXITCODE = $null
        $lines = @(& $File @Arguments 2>&1 | ForEach-Object { "$_" })
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $ErrorActionPreference = $previous
    }
    [IO.File]::WriteAllLines($log, [string[]]$lines, $utf8)
    $pass = @($lines | Where-Object { $_ -like 'PASS *' }).Count
    $fail = @($lines | Where-Object { $_ -like 'FAIL*' }).Count
    $ok = ($code -eq 0) -and ($fail -eq 0)
    [void]$steps.Add([ordered]@{ step = $Name; ok = $ok; exit = $code; pass = $pass; fail = $fail; log = $log })
    $mark = 'FAIL'
    if ($ok) { $mark = 'ok  ' }
    # Write-Host: the function's pipeline output is its return value.
    Write-Host ("{0} {1,-22} exit={2} pass={3} fail={4}" -f $mark, $Name, $code, $pass, $fail)
    foreach ($line in ($lines | Where-Object { $_ -like 'FAIL*' -or $_ -like 'REFUSED*' } | Select-Object -First 5)) {
        Write-Host "     $line"
    }
    return $ok
}

# 1. Candidate
if ($Candidate) { $Candidate = [IO.Path]::GetFullPath($Candidate) }
else {
    $Candidate = Join-Path $OutputDir 'candidate'
    [void](Invoke-Step 'build-candidate' 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $repo 'scripts\build_ja.ps1'), '-OutputDir', $Candidate, '-Python', $Python))
}
$dll = Join-Path $Candidate 'CMOLocalizerJa.dll'
$json = Join-Path $Candidate 'CMOLocalizerJa.json'

# 2. Chinese engine for the isolation probe's negative control
$zhDll = $null
if (-not $SkipChineseControl) {
    $zhDir = Join-Path $OutputDir 'zh-engine'
    if (Invoke-Step 'build-zh-control' 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            (Join-Path $repo 'scripts\build.ps1'), '-OutputDir', $zhDir)) {
        $zhDll = Join-Path $zhDir 'CMOLocalizer.dll'
    }
}

# 3. Fixture
$fixtureDir = Join-Path $OutputDir 'fixture'
New-Item -ItemType Directory -Path $fixtureDir | Out-Null
$fixture = Join-Path $fixtureDir 'fixture.exe'
[void](Invoke-Step 'compile-fixture' $compiler @('-nologo', '-utf8output', '-codepage:65001', '-target:exe',
    '-platform:anycpu', "-out:$fixture", '-reference:System.dll', '-reference:System.Core.dll',
    '-reference:System.Drawing.dll', '-reference:System.Windows.Forms.dll',
    (Join-Path $repo 'src\tests\JapaneseLocalizationTests.cs')))

if ((Test-Path -LiteralPath $fixture) -and (Test-Path -LiteralPath $dll)) {
    # 4. In-process fixtures
    [void](Invoke-Step 'unit' $fixture @('unit', $dll, $json, (Join-Path $OutputDir 'unit-scratch')))
    $isolationArgs = @('isolation', $dll)
    if ($zhDll) { $isolationArgs += $zhDll }
    [void](Invoke-Step 'isolation' $fixture $isolationArgs)

    # 5. End to end: patch a synthetic game directory, start its Command.exe, restore.
    $game = Join-Path $OutputDir 'host-game'
    New-Item -ItemType Directory -Path $game | Out-Null
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $game 'Command.exe')
    $configPath = Join-Path $game 'Command.exe.config'
    $configText = "<?xml version=""1.0"" encoding=""utf-8""?>`r`n<configuration>`r`n" +
        "  <startup useLegacyV2RuntimeActivationPolicy=""true"">`r`n" +
        "    <supportedRuntime version=""v4.0"" sku="".NETFramework,Version=v4.8"" />`r`n  </startup>`r`n" +
        "<runtime>`r`n`t  <enforceFIPSPolicy enabled=""false"" />`r`n</runtime>`r`n</configuration>`r`n"
    [IO.File]::WriteAllText($configPath, $configText, (New-Object Text.UTF8Encoding($true)))
    $originalConfig = Get-Sha256 $configPath
    $patcher = Join-Path $repo 'scripts\manage_ja_patch.py'
    $applied = Invoke-Step 'host-apply' $Python @('-B', '-X', 'utf8', $patcher, 'apply', '--game-dir', $game,
        '--build-dir', $Candidate, '--yes')
    if ($applied) {
        [void](Invoke-Step 'host-run' (Join-Path $game 'Command.exe') @('host') $game)
        [void](Invoke-Step 'host-restore' $Python @('-B', '-X', 'utf8', $patcher, 'restore', '--game-dir', $game, '--yes'))
        $restored = (Get-Sha256 $configPath) -eq $originalConfig
        $removed = -not (Test-Path -LiteralPath (Join-Path $game 'CMOLocalizerJa.dll')) -and
            -not (Test-Path -LiteralPath (Join-Path $game 'CMOLocalizerJa.json'))
        $mark = 'FAIL'
        if ($restored -and $removed) { $mark = 'ok  ' }
        [void]$steps.Add([ordered]@{ step = 'host-restore-bytes'; ok = ($restored -and $removed); exit = 0;
            pass = [int]$restored + [int]$removed; fail = 2 - [int]$restored - [int]$removed; log = '' })
        Write-Output ("{0} {1,-22} config byte-identical={2} payload removed={3}" -f $mark, 'host-restore-bytes', $restored, $removed)
    }
}

# 6. Python suites
[void](Invoke-Step 'dictionary-tests' $Python @('-B', '-X', 'utf8', (Join-Path $repo 'src\tests\JapaneseDictionaryTests.py')))
[void](Invoke-Step 'deployment-tests' $Python @('-B', '-X', 'utf8', (Join-Path $repo 'src\tests\JapaneseDeploymentTests.py')))

# 7. Report
$hashes = [ordered]@{}
foreach ($path in @($dll, $json, $fixture, $zhDll)) {
    if ($path -and (Test-Path -LiteralPath $path)) { $hashes[$path] = Get-Sha256 $path }
}
$failed = @($steps | Where-Object { -not $_.ok }).Count
$report = [ordered]@{
    generated = (Get-Date).ToString('s')
    commit = "$(& git -C $repo rev-parse HEAD 2>$null)"
    candidate = $Candidate
    ok = ($failed -eq 0)
    steps = $steps
    sha256 = $hashes
}
$reportPath = Join-Path $OutputDir 'test-report.json'
[IO.File]::WriteAllText($reportPath, (($report | ConvertTo-Json -Depth 5) -replace "`r`n", "`n") + "`n", $utf8)
Write-Output "report: $reportPath"
if ($failed -ne 0) {
    Write-Output "Japanese tests FAILED ($failed step(s))"
    exit 1
}
Write-Output "Japanese tests PASSED ($($steps.Count) steps)"

# Japanese minimal UI candidate build. Never deploys, never touches the game,
# never writes inside dict/, and never overwrites a directory it did not create.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build_ja.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build_ja.ps1 -OutputDir build/ja-candidate -Replace
#
# Output: CMOLocalizerJa.dll (compiled from src/ja/*.cs ONLY, so no Chinese engine
# code is present), CMOLocalizerJa.json and
# ja-candidate.manifest.json (SHA256 of both, source commit, CLR entry point).
[CmdletBinding()]
param(
    [string]$OutputDir,
    [switch]$Replace,
    [string]$Compiler = (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDir) { $OutputDir = Join-Path $repo 'build\ja-candidate' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$manifestName = 'ja-candidate.manifest.json'
$sourceDir = Join-Path $repo 'src\ja'
$sources = @(Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
$assemblyName = 'CMOLocalizerJa'
$profileName = 'ja-minimal-ui-v1'
$domainManagerType = 'CMOLocalizer.Japanese.JapaneseDomainManager'
$assemblyIdentity = "$assemblyName, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"

if ($sources.Count -eq 0) { throw "No Japanese engine sources in $sourceDir" }
foreach ($required in @($Compiler) + $sources) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing build input: $required" }
}
$dictRoot = [IO.Path]::GetFullPath((Join-Path $repo 'dict'))
if ($OutputDir.StartsWith($dictRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to build inside dict/: $OutputDir"
}
if (Test-Path -LiteralPath $OutputDir) {
    $marker = Join-Path $OutputDir $manifestName
    if (-not $Replace) { throw "Output directory exists (use -Replace to rebuild a previous candidate): $OutputDir" }
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        throw "Refusing to replace a directory that is not a Japanese candidate build: $OutputDir"
    }
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('cmo-ja-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $assemblyInfo = Join-Path $temp 'AssemblyInfo.cs'
    $info = @(
        '[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]',
        '[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]',
        "[assembly: System.Reflection.AssemblyTitle(""CMO Japanese UI ($profileName)"")]",
        "[assembly: System.Reflection.AssemblyInformationalVersion(""$profileName"")]")
    [IO.File]::WriteAllLines($assemblyInfo, $info, (New-Object Text.UTF8Encoding($false)))

    $dll = Join-Path $OutputDir "$assemblyName.dll"
    $arguments = @('-nologo', '-utf8output', '-codepage:65001', '-target:library', '-platform:anycpu',
        '-optimize+', "-out:$dll", '-reference:System.dll', '-reference:System.Core.dll',
        '-reference:System.Drawing.dll', '-reference:System.Windows.Forms.dll') + $sources + @($assemblyInfo)
    & $Compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Japanese engine compilation failed (exit $LASTEXITCODE)." }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

$json = Join-Path $OutputDir "$assemblyName.json"
& $Python -B -X utf8 (Join-Path $repo 'scripts\japanese_dict.py') --out $json
if ($LASTEXITCODE -ne 0) { throw "Japanese dictionary build failed (exit $LASTEXITCODE)." }

$commit = (& git -C $repo rev-parse HEAD 2>$null)
$dirty = [bool](& git -C $repo status --porcelain -- src/ja dict/src/ja_menu.json dict/src/ja_options.json scripts/japanese_dict.py 2>$null)
$manifest = [ordered]@{
    profile = $profileName
    assembly = $assemblyIdentity
    domain_manager_type = $domainManagerType
    source_commit = "$commit"
    source_dirty = $dirty
    game_build = (Get-Content -LiteralPath (Join-Path $repo 'GAME_BUILD') -Raw).Trim()
    files = [ordered]@{
        "$assemblyName.dll" = [ordered]@{ sha256 = (Get-Sha256 $dll); bytes = (Get-Item -LiteralPath $dll).Length }
        "$assemblyName.json" = [ordered]@{ sha256 = (Get-Sha256 $json); bytes = (Get-Item -LiteralPath $json).Length }
    }
}
$manifestText = ($manifest | ConvertTo-Json -Depth 4) -replace "`r`n", "`n"
[IO.File]::WriteAllText((Join-Path $OutputDir $manifestName), $manifestText + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Output "Japanese candidate built: $OutputDir"
Write-Output ("  {0}.dll  sha256={1}" -f $assemblyName, $manifest.files["$assemblyName.dll"].sha256)
Write-Output ("  {0}.json sha256={1}" -f $assemblyName, $manifest.files["$assemblyName.json"].sha256)

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]
    [string] $RuntimeIdentifier,
    [string] $ReleaseVersion = '0.19.0-preview.1',
    [switch] $AllowCrossBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$global:LASTEXITCODE = 0

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot '.artifacts/m19-5-1'
$output = Join-Path $artifactRoot "$ReleaseVersion/$RuntimeIdentifier"
$publish = Join-Path $output 'publish'
$package = Join-Path $output 'package'
$shellProject = Join-Path $repositoryRoot 'src/Shell/Junexis.Studio.Shell.csproj'
$platforms = @{
    'win-x64' = [ordered]@{ system = 'windows'; architecture = 'x64'; launcher = 'junexis-studio.exe'; extension = '.zip' }
    'linux-x64' = [ordered]@{ system = 'linux'; architecture = 'x64'; launcher = 'junexis-studio'; extension = '.tar.gz' }
    'osx-arm64' = [ordered]@{ system = 'macos'; architecture = 'arm64'; launcher = 'junexis-studio'; extension = '.tar.gz' }
}

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-NativeRid {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    if ($IsWindows -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) { return 'win-x64' }
    if ($IsLinux -and $architecture -eq [Runtime.InteropServices.Architecture]::X64) { return 'linux-x64' }
    if ($IsMacOS -and $architecture -eq [Runtime.InteropServices.Architecture]::Arm64) { return 'osx-arm64' }
    return $null
}

function Invoke-StudioProbe([string] $Launcher, [string[]] $Arguments) {
    if ($IsWindows) {
        $process = Start-Process -FilePath $Launcher -ArgumentList $Arguments -Wait -PassThru -WindowStyle Hidden
        return $process.ExitCode
    }
    $probeOutput = @(& $Launcher @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $probeOutput | ForEach-Object { Write-Host $_ }
    return $exitCode
}

$nativeRid = Get-NativeRid
$isNativeBuild = $nativeRid -ceq $RuntimeIdentifier
if (-not $isNativeBuild -and -not $AllowCrossBuild) {
    throw "Dowód dla $RuntimeIdentifier musi powstać na natywnym wykonawcy. Bieżący RID: $($nativeRid ?? 'niewspierany')."
}

$resolvedArtifactRoot = [IO.Path]::GetFullPath($artifactRoot).TrimEnd('\', '/')
$resolvedOutput = [IO.Path]::GetFullPath($output).TrimEnd('\', '/')
Assert-Condition ($resolvedOutput.StartsWith($resolvedArtifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
    "Katalog wynikowy musi pozostać wewnątrz $resolvedArtifactRoot."
if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
New-Item -ItemType Directory -Path $publish, $package -Force | Out-Null

& dotnet publish $shellProject -c Release -r $RuntimeIdentifier --self-contained true -o $publish `
    --nologo -v minimal -p:Version=$ReleaseVersion
if ($LASTEXITCODE -ne 0) { throw "Publikacja Studio dla $RuntimeIdentifier zakończyła się kodem $LASTEXITCODE." }

$platform = $platforms[$RuntimeIdentifier]
$publishedLauncher = Join-Path $publish $(if ($RuntimeIdentifier -ceq 'win-x64') { 'Junexis.Studio.Shell.exe' } else { 'Junexis.Studio.Shell' })
Assert-Condition (Test-Path -LiteralPath $publishedLauncher -PathType Leaf) 'Publikacja nie zawiera samodzielnego programu Studio.'

if ($RuntimeIdentifier -ceq 'osx-arm64') {
    $contents = Join-Path $package 'Junexis Studio.app/Contents'
    $payload = Join-Path $contents 'MacOS'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination $payload -Recurse -Force
    Rename-Item -LiteralPath (Join-Path $payload 'Junexis.Studio.Shell') -NewName $platform.launcher
    @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleExecutable</key><string>junexis-studio</string>
  <key>CFBundleIdentifier</key><string>dev.junexis.studio</string>
  <key>CFBundleName</key><string>Junexis Studio</string>
  <key>CFBundleShortVersionString</key><string>$ReleaseVersion</string>
  <key>CFBundlePackageType</key><string>APPL</string>
</dict></plist>
"@ | Set-Content -LiteralPath (Join-Path $contents 'Info.plist') -Encoding utf8NoBOM
    $entrypoint = 'Junexis Studio.app/Contents/MacOS/junexis-studio'
}
else {
    Copy-Item -Path (Join-Path $publish '*') -Destination $package -Recurse -Force
    Rename-Item -LiteralPath (Join-Path $package $(Split-Path -Leaf $publishedLauncher)) -NewName $platform.launcher
    $entrypoint = $platform.launcher
}

$checks = @()
if ($isNativeBuild) {
    $nativeLauncher = Join-Path $package $entrypoint
    $distributionOutput = Join-Path $output 'distribution-probe.json'
    $probeExitCode = Invoke-StudioProbe $nativeLauncher @('--distribution-manifest', $RuntimeIdentifier, $entrypoint, 'true', $distributionOutput)
    Assert-Condition ($probeExitCode -eq 0 -and (Test-Path -LiteralPath $distributionOutput -PathType Leaf)) `
        'Samodzielne Studio nie utworzyło manifestu dystrybucji.'
    $distribution = Get-Content -LiteralPath $distributionOutput -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($distribution.runtimeIdentifier -ceq $RuntimeIdentifier -and $distribution.selfContained -eq $true) `
        'Manifest Studio błędnie opisuje wariant dystrybucji.'
    $checks += [ordered]@{ component = 'studio'; command = 'junexis-studio --distribution-manifest'; exitCode = 0 }

    $profileBin = Join-Path $output 'profile-bin'
    New-Item -ItemType Directory -Path $profileBin -Force | Out-Null
    $profileCli = Join-Path $profileBin $(if ($IsWindows) { 'junexis.exe' } else { 'junexis' })
    New-Item -ItemType File -Path $profileCli -Force | Out-Null
    $previousProfileBin = $env:JUNEXIS_PROFILE_BIN
    try {
        $env:JUNEXIS_PROFILE_BIN = $profileBin
        $profileOutput = Join-Path $output 'profile-probe.json'
        $probeExitCode = Invoke-StudioProbe $nativeLauncher @('--profile-probe', $profileOutput)
        Assert-Condition ($probeExitCode -eq 0 -and (Test-Path -LiteralPath $profileOutput -PathType Leaf)) `
            'Studio nie utworzyło wyniku próby profilu.'
        $profileProbe = Get-Content -LiteralPath $profileOutput -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert-Condition ([IO.Path]::GetFullPath($profileProbe.command) -ceq [IO.Path]::GetFullPath($profileCli)) `
            'Studio nie odnalazło CLI w stabilnym katalogu profilu.'
    }
    finally { $env:JUNEXIS_PROFILE_BIN = $previousProfileBin }
    $checks += [ordered]@{ component = 'profile'; command = 'junexis-studio --profile-probe'; exitCode = 0 }
}

$archiveName = "junexis-studio-$ReleaseVersion-$RuntimeIdentifier$($platform.extension)"
$archivePath = Join-Path $output $archiveName
if ($platform.extension -ceq '.zip') {
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archivePath -CompressionLevel Optimal
}
else {
    $tar = Get-Command tar -ErrorAction SilentlyContinue
    Assert-Condition ($null -ne $tar) 'Pakowanie tar.gz wymaga programu tar.'
    & $tar.Source -czf $archivePath -C $package .
    if ($LASTEXITCODE -ne 0) { throw "Pakowanie $RuntimeIdentifier zakończyło się kodem $LASTEXITCODE." }
}

$archive = Get-Item -LiteralPath $archivePath
$receipt = [ordered]@{
    schemaVersion = 'junexis.studio-build-receipt/0.1'
    releaseVersion = $ReleaseVersion
    rid = $RuntimeIdentifier
    system = $platform.system
    architecture = $platform.architecture
    entrypoint = $entrypoint
    selfContained = $true
    artifact = [ordered]@{
        file = $archive.Name
        sizeBytes = $archive.Length
        sha256 = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    proof = [ordered]@{
        kind = $(if ($isNativeBuild) { 'native-studio-smoke' } else { 'cross-build-only' })
        hostRid = $nativeRid
        checks = $checks
    }
}
$receiptJson = $receipt | ConvertTo-Json -Depth 8
$receiptJson | Set-Content -LiteralPath (Join-Path $output 'junexis.studio-build-receipt.json') -Encoding utf8NoBOM
if ($isNativeBuild) {
    $evidenceRoot = Join-Path $repositoryRoot 'evidence/m19-5-1'
    New-Item -ItemType Directory -Path $evidenceRoot -Force | Out-Null
    $receiptJson | Set-Content -LiteralPath (Join-Path $evidenceRoot "$RuntimeIdentifier.json") -Encoding utf8NoBOM
}

Write-Output "M19.5.1 UTWORZONO: $archivePath"
Write-Output "M19.5.1 DOWÓD: $($receipt.proof.kind) dla $RuntimeIdentifier"

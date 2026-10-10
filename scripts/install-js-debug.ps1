[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$version = '1.117.0'
$expectedSha256 = 'AD8D04EDE9D4B75CC290FD5438A65047A06F786D04F604B6112485B36F090772'
$uri = "https://github.com/microsoft/vscode-js-debug/releases/download/v$version/js-debug-dap-v$version.tar.gz"
$root = Join-Path $env:LOCALAPPDATA 'Junexis\toolchains\js-debug'
$target = Join-Path $root $version
$server = Join-Path $target 'js-debug\src\dapDebugServer.js'

if (Test-Path -LiteralPath $server) {
    Write-Output "js-debug $version is already installed at $target"
    return
}
if (Test-Path -LiteralPath $target) {
    throw "Refusing incomplete existing installation: $target"
}

$archive = Join-Path ([System.IO.Path]::GetTempPath()) "junexis-js-debug-$version-$([Guid]::NewGuid()).tar.gz"
try {
    Invoke-WebRequest -UseBasicParsing $uri -OutFile $archive
    $actualSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
    if (-not $actualSha256.Equals($expectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 mismatch for js-debug $version. Expected $expectedSha256, got $actualSha256."
    }

    New-Item -ItemType Directory -Path $target -Force | Out-Null
    tar -xzf $archive -C $target
    if (-not (Test-Path -LiteralPath $server)) {
        throw "The official archive did not contain js-debug/src/dapDebugServer.js."
    }
    Write-Output "Installed js-debug $version at $target"
}
finally {
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
}

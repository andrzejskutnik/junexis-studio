[CmdletBinding()]
param(
    [string] $ReleaseVersion = '0.19.0-preview.1',
    [switch] $BuildAll,
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$global:LASTEXITCODE = 0
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publisher = Join-Path $PSScriptRoot 'publish-m19-5-1.ps1'
$solution = Join-Path $repositoryRoot 'JunexisStudio.sln'
$rids = @('win-x64', 'linux-x64', 'osx-arm64')

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$testArguments = @($solution, '--nologo', '-v', 'minimal')
if ($NoRestore) { $testArguments += '--no-restore' }
& dotnet test @testArguments
if ($LASTEXITCODE -ne 0) { throw "Testy Studio zakończyły się kodem $LASTEXITCODE." }

if ($BuildAll) {
    foreach ($rid in $rids) {
        & $publisher -RuntimeIdentifier $rid -ReleaseVersion $ReleaseVersion -AllowCrossBuild
        if ($LASTEXITCODE -ne 0) { throw "Budowanie wariantu $rid zakończyło się kodem $LASTEXITCODE." }
    }
}

foreach ($rid in $rids) {
    $variantRoot = Join-Path $repositoryRoot ".artifacts/m19-5-1/$ReleaseVersion/$rid"
    $receiptPath = Join-Path $variantRoot 'junexis.studio-build-receipt.json'
    Assert-Condition (Test-Path -LiteralPath $receiptPath -PathType Leaf) "Brakuje rachunku Studio dla $rid."
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($receipt.schemaVersion -ceq 'junexis.studio-build-receipt/0.1') "Rachunek $rid ma nieznany kontrakt."
    Assert-Condition ($receipt.rid -ceq $rid -and $receipt.selfContained -eq $true) "Rachunek $rid błędnie opisuje paczkę."
    $expectedEntrypoint = if ($rid -ceq 'win-x64') {
        'junexis-studio.exe'
    }
    elseif ($rid -ceq 'osx-arm64') {
        'Junexis Studio.app/Contents/MacOS/junexis-studio'
    }
    else {
        'junexis-studio'
    }
    Assert-Condition ($receipt.entrypoint -ceq $expectedEntrypoint) "Wariant $rid ma niewłaściwy punkt wejścia."
    Assert-Condition (Test-Path -LiteralPath (Join-Path $variantRoot "package/$expectedEntrypoint") -PathType Leaf) `
        "Paczka $rid nie zawiera deklarowanego punktu wejścia."
    if ($rid -ceq 'osx-arm64') {
        Assert-Condition (Test-Path -LiteralPath (Join-Path $variantRoot 'package/Junexis Studio.app/Contents/Info.plist') -PathType Leaf) `
            'Paczka macOS nie ma kompletnej struktury aplikacji .app.'
    }
    $archivePath = Join-Path $variantRoot $receipt.artifact.file
    Assert-Condition (Test-Path -LiteralPath $archivePath -PathType Leaf) "Brakuje archiwum Studio dla $rid."
    Assert-Condition ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $receipt.artifact.sha256) `
        "SHA-256 archiwum $rid nie zgadza się z rachunkiem."
    if ($receipt.proof.kind -ceq 'native-studio-smoke') {
        Assert-Condition ($receipt.proof.hostRid -ceq $rid -and @($receipt.proof.checks).Count -eq 2) `
            "Natywny rachunek $rid nie zawiera obu prób Studio."
        Assert-Condition (@($receipt.proof.checks | Where-Object { $_.exitCode -ne 0 }).Count -eq 0) `
            "Natywny rachunek $rid zawiera nieudaną próbę Studio."
    }
    elseif ($receipt.proof.kind -ceq 'cross-build-only') {
        Assert-Condition ($receipt.proof.hostRid -cne $rid -and @($receipt.proof.checks).Count -eq 0) `
            "Krzyżowy rachunek $rid udaje próbę natywną."
    }
    else { throw "Wariant $rid ma nieznany rodzaj dowodu." }
}

$nativeEvidence = @()
foreach ($rid in $rids) {
    $evidencePath = Join-Path $repositoryRoot "evidence/m19-5-1/$rid.json"
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { continue }
    $evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($evidence.rid -ceq $rid -and $evidence.proof.kind -ceq 'native-studio-smoke' `
        -and $evidence.proof.hostRid -ceq $rid -and @($evidence.proof.checks).Count -eq 2) `
        "Wersjonowany dowód $rid nie opisuje natywnej próby Studio."
    Assert-Condition (@($evidence.proof.checks | Where-Object { $_.exitCode -ne 0 }).Count -eq 0) `
        "Wersjonowany dowód $rid zawiera nieudaną próbę Studio."
    $receiptPath = Join-Path $repositoryRoot ".artifacts/m19-5-1/$ReleaseVersion/$rid/junexis.studio-build-receipt.json"
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Condition ($evidence.artifact.sha256 -ceq $receipt.artifact.sha256) `
        "Wersjonowany dowód $rid nie odpowiada bieżącemu archiwum."
    $nativeEvidence += $rid
}

Write-Output 'M19.5.1 ZAIMPLEMENTOWANO: trzy samodzielne paczki Studio i jawny kontrakt profilu narzędzi.'
$missingEvidence = @($rids | Where-Object { $_ -notin $nativeEvidence })
if ($missingEvidence.Count -eq 0) {
    Write-Output 'M19.5.1 VERIFIED: każdy wariant ma wersjonowany dowód z natywnego wykonawcy.'
}
else {
    Write-Output "Status VERIFIED wymaga jeszcze natywnych dowodów z: $($missingEvidence -join ', ')."
}

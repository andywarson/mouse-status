param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$InstallerCompiler
)
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'VERSION must contain a semantic version in X.Y.Z form.'
}

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$destination = (Resolve-Path -LiteralPath $OutputDirectory).Path
$staging = Join-Path ([IO.Path]::GetTempPath()) ('MouseStatusPackage-' + [Guid]::NewGuid().ToString('N'))
$binary = Join-Path $staging 'MouseStatus.exe'
$prefix = "MouseStatus-$version-windows-x64"
$setupName = "$prefix-setup"
$setupStage = Join-Path $staging "$setupName.exe"
try {
    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $staging
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

    if (!$InstallerCompiler) {
        $InstallerCompiler = & (Join-Path $PSScriptRoot 'installer/Get-Compiler.ps1')
    }
    $compiler = (Resolve-Path -LiteralPath $InstallerCompiler).Path
    $definition = Join-Path $PSScriptRoot 'installer/MouseStatus.iss'
    & $compiler /Qp "/O$staging" "/DAppVersion=$version" "/DAppBinary=$binary" "/DAssetName=$setupName" $definition
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setupStage -PathType Leaf)) {
        throw 'Installer build failed.'
    }

    $portable = Join-Path $destination "$prefix-portable.exe"
    $setup = Join-Path $destination "$setupName.exe"
    $checksums = Join-Path $destination "MouseStatus-$version-sha256.txt"
    Copy-Item -LiteralPath $binary -Destination $portable -Force
    Copy-Item -LiteralPath $setupStage -Destination $setup -Force
    @($setup, $portable) | ForEach-Object {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($_))"
    } | Set-Content -LiteralPath $checksums -Encoding ASCII
    Get-Item -LiteralPath $setup, $portable, $checksums
} finally {
    if (Test-Path -LiteralPath $staging) {
        foreach ($file in @($binary, $setupStage)) {
            if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file }
        }
        [IO.Directory]::Delete($staging)
    }
}

param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'VERSION must contain a semantic version in X.Y.Z form.'
}

$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$destination = (Resolve-Path -LiteralPath $OutputDirectory).Path
$staging = Join-Path ([IO.Path]::GetTempPath()) ('MaicongPackage-' + [Guid]::NewGuid().ToString('N'))
try {
    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $staging
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

    $files = @((Join-Path $staging 'MaicongMouseStatus.exe'))
    foreach ($name in @('README.md', 'README.en.md', 'CHANGELOG.md', 'LICENSE')) {
        $path = Join-Path $PSScriptRoot $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { $files += $path }
    }

    $name = "MaicongMouseStatus-$version-windows-x64.zip"
    $archive = Join-Path $destination $name
    Compress-Archive -LiteralPath $files -DestinationPath $archive -CompressionLevel Optimal -Force
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    "$hash  $name" | Set-Content -LiteralPath "$archive.sha256" -Encoding ASCII
    Get-Item -LiteralPath $archive, "$archive.sha256"
} finally {
    if (Test-Path -LiteralPath $staging) {
        $executable = Join-Path $staging 'MaicongMouseStatus.exe'
        if (Test-Path -LiteralPath $executable) { Remove-Item -LiteralPath $executable }
        [IO.Directory]::Delete($staging)
    }
}

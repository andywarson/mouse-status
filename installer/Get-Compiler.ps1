$ErrorActionPreference = 'Stop'
$version = '7.1.1'
$hash = '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe'
$repo = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $repo ".installer-tools/InnoSetup-$version"
$compiler = Join-Path $cache 'ISCC.exe'
if (Test-Path -LiteralPath $compiler -PathType Leaf) { return $compiler }

$download = Join-Path ([IO.Path]::GetTempPath()) ('InnoSetup-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    Invoke-WebRequest -Uri $url -OutFile $download -TimeoutSec 90 -UseBasicParsing
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $download).Hash -ne $hash) {
        throw 'The Inno Setup compiler download did not match its pinned SHA-256.'
    }
    $arguments = '/CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /SP- /DIR="' + $cache + '"'
    $process = Start-Process -FilePath $download -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler -PathType Leaf)) {
        throw "Inno Setup compiler installation failed with exit code $($process.ExitCode)."
    }
    $compiler
} finally {
    if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download }
}

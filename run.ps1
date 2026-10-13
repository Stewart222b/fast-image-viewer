$ErrorActionPreference = 'Stop'
try {
    $exe = Join-Path $PSScriptRoot 'dist\FastImageViewer.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        $powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    Start-Process -FilePath $exe -WorkingDirectory $PSScriptRoot
    exit 0
} catch {
    Write-Error $_
    exit 1
}

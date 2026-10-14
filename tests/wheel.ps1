$ErrorActionPreference = 'Stop'
try {
    $root = Split-Path -Parent $PSScriptRoot
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $output = Join-Path $root 'artifacts'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $exe = Join-Path $output 'WheelZoomTests.exe'
    $arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/langversion:5', ('/out:' + $exe), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xaml.dll', '/reference:System.Drawing.dll')
    foreach ($assembly in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')) {
        $arguments += '/reference:' + (Join-Path $wpf $assembly)
    }
    $core = @(Get-ChildItem -LiteralPath (Join-Path $root 'core') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    & $compiler @arguments (Join-Path $PSScriptRoot 'WheelZoomTests.cs') (Join-Path $root 'src\ViewerCore.cs') (Join-Path $root 'src\WheelZoom.cs') (Join-Path $root 'src\ImageSurface.cs') @core
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item -LiteralPath (Join-Path $root 'src\App.config') -Destination ($exe + '.config') -Force
    & $exe
    exit $LASTEXITCODE
} catch {
    Write-Error $_
    exit 1
}

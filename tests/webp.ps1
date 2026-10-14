$ErrorActionPreference = 'Stop'
$previousCodecDirectory = $env:FAST_IMAGE_VIEWER_CODEC_DIRECTORY
try {
    $root = Split-Path -Parent $PSScriptRoot
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    $output = Join-Path $root ('artifacts\webp-tests-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $env:FAST_IMAGE_VIEWER_CODEC_DIRECTORY = Join-Path $output 'codecs'
    $exe = Join-Path $output 'WebpTests.exe'
    $arguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/langversion:5', ('/out:' + $exe), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xaml.dll', ('/resource:' + (Join-Path $root 'third-party\libwebp\FastImageViewerWebp.dll') + ',FastImageViewer.Codecs.Webp'))
    foreach ($assembly in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')) { $arguments += '/reference:' + (Join-Path $wpf $assembly) }
    $core = @(Get-ChildItem -LiteralPath (Join-Path $root 'core') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    & $compiler @arguments (Join-Path $PSScriptRoot 'WebpTests.cs') (Join-Path $root 'src\ViewerCore.cs') (Join-Path $root 'src\WebpDecoder.cs') @core
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item -LiteralPath (Join-Path $root 'src\App.config') -Destination ($exe + '.config') -Force
    & $exe (Join-Path $PSScriptRoot 'fixtures\webp') $output
    exit $LASTEXITCODE
} catch { Write-Error $_; exit 1 }
finally { $env:FAST_IMAGE_VIEWER_CODEC_DIRECTORY = $previousCodecDirectory }

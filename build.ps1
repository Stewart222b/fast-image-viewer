param([string]$OutputDirectory = 'dist')
$ErrorActionPreference = 'Stop'
try {
    $root = $PSScriptRoot
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework x64 C# compiler was not found.' }
    $output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
    $workspacePrefix = $root.TrimEnd('\') + '\'
    if (-not $output.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build output must be inside the project directory.' }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'core') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
    if ($sources.Count -eq 0) { throw 'No C# source files were found in src.' }
    $arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/langversion:5', ('/out:' + (Join-Path $output 'FastImageViewer.exe')), ('/win32manifest:' + (Join-Path $root 'app.manifest')), '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xaml.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll')
    $arguments += '/resource:' + (Join-Path $root 'third-party\libwebp\FastImageViewerWebp.dll') + ',FastImageViewer.Codecs.Webp'
    foreach ($assembly in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')) {
        $arguments += '/reference:' + (Join-Path $wpf $assembly)
    }
    & $compiler @arguments @sources
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Copy-Item -LiteralPath (Join-Path $root 'src\App.config') -Destination (Join-Path $output 'FastImageViewer.exe.config') -Force
    $noticeDirectory = Join-Path $output 'third-party\libwebp'
    New-Item -ItemType Directory -Path $noticeDirectory -Force | Out-Null
    foreach ($notice in @('LICENSE', 'PATENTS', 'AUTHORS', 'NOTICE.md')) {
        Copy-Item -LiteralPath (Join-Path $root ('third-party\libwebp\' + $notice)) -Destination (Join-Path $noticeDirectory $notice) -Force
    }
    Write-Host ('Built ' + (Join-Path $output 'FastImageViewer.exe'))
    exit 0
} catch {
    Write-Error $_
    exit 1
}

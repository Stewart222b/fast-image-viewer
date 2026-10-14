$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$exe = Join-Path $output 'ImageLoadCacheTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 ('/out:' + $exe) /reference:System.dll /reference:System.Core.dll (Join-Path $root 'core\ImageFiles.cs') (Join-Path $root 'core\ImageLoadCache.cs') (Join-Path $PSScriptRoot 'ImageLoadCacheTests.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $exe $output
exit $LASTEXITCODE

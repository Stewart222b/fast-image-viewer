param([Parameter(Mandatory = $true)][string]$SourceArchive, [string]$Compiler = 'gcc')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$archive = (Resolve-Path -LiteralPath $SourceArchive).Path
$expected = 'E4AB7009BF0629FD11982D4C2AA83964CF244CFFBA7347ECD39019A9E38C4564'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'Unexpected libwebp 1.6.0 source archive hash.' }
$sourceRoot = Join-Path $root 'artifacts\webp-source'
$buildRoot = Join-Path $root 'artifacts\webp-native'
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null
tar -xzf $archive -C $sourceRoot
if ($LASTEXITCODE -ne 0) { throw 'libwebp extraction failed.' }
$source = Join-Path $sourceRoot 'libwebp-1.6.0'
$compilerPath = (Get-Command $Compiler -ErrorAction Stop).Source
cmake -S $PSScriptRoot -B $buildRoot -G Ninja ("-DLIBWEBP_SOURCE=" + $source.Replace('\','/')) ("-DCMAKE_C_COMPILER=" + $compilerPath.Replace('\','/')) -DCMAKE_BUILD_TYPE=Release
if ($LASTEXITCODE -ne 0) { throw 'libwebp configuration failed.' }
cmake --build $buildRoot --target FastImageViewerWebp --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'libwebp compilation failed.' }
$destination = Join-Path $root 'third-party\libwebp\FastImageViewerWebp.dll'
Copy-Item -LiteralPath (Join-Path $buildRoot 'FastImageViewerWebp.dll') -Destination $destination -Force
$hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
$decoderPath = Join-Path $root 'src\WebpDecoder.cs'
$decoder = [IO.File]::ReadAllText($decoderPath)
$decoder = [regex]::Replace($decoder, 'private const string CodecHash = "[A-F0-9]+";', ('private const string CodecHash = "' + $hash + '";'))
[IO.File]::WriteAllText($decoderPath, $decoder, [Text.UTF8Encoding]::new($true))
Write-Host "Built the embedded x64 codec; updated SHA256: $hash"

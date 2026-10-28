param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish (Join-Path $projectRoot 'PromptDC.csproj') -c Release -r win-x64 --self-contained $selfContained -o (Join-Path $projectRoot 'dist/PromptDC') -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $projectRoot 'dist/PromptDC/README.md')

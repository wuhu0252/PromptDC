param([string]$OutputDirectory = 'dist/releases')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'PromptDC.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected an explicit major.minor.patch project version' }
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$archiveName = "PromptDC-$version-win-x64.zip"
$archivePath = Join-Path $outputPath $archiveName
$checksumPath = Join-Path $outputPath 'SHA256SUMS.txt'
if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw 'Release artifacts already exist. Choose a new output directory to avoid overwriting them.'
}
# Always publish into a fresh staging directory; never package an existing user's app folder.
$stagingPath = Join-Path $projectRoot ('dist/package-' + [guid]::NewGuid().ToString('N'))
$appPath = Join-Path $stagingPath 'PromptDC'
& (Join-Path $PSScriptRoot 'publish.ps1') -OutputDirectory $appPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

$runtime = Get-Content -LiteralPath (Join-Path $appPath 'PromptDC.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) {
    throw 'The release must include the .NET runtime'
}
foreach ($required in @('PromptDC.exe', 'PromptDC.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Windows.Forms.dll', 'UIAutomationClient.dll', 'README.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $appPath $required) -PathType Leaf)) { throw "Missing release file: $required" }
}
$forbidden = Get-ChildItem -LiteralPath $appPath -Recurse -Force | Where-Object {
    $_.Name -match '^(settings\.json(?:\..*)?|secrets\.json|\.env(?:\..*)?|\.git|node_modules|obj|bin|test-results)$' -or
    $_.Extension -in @('.pdb', '.key', '.pem', '.pfx', '.p12', '.log', '.sqlite', '.jsonl')
}
if ($forbidden) { throw 'Unexpected settings, credentials, diagnostics or development files in release staging' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
Compress-Archive -LiteralPath $appPath -DestinationPath $archivePath -CompressionLevel Optimal
$digest = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText($checksumPath, "$digest  $archiveName`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "Archive: $archivePath"
Write-Output "SHA256: $digest"
Write-Output "Checksums: $checksumPath"

param(
    [switch]$FrameworkDependent,
    [string]$OutputDirectory = 'dist/PromptDC'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish (Join-Path $projectRoot 'PromptDC.csproj') -c Release -r win-x64 --self-contained $selfContained -o $outputPath -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
foreach ($document in @('LICENSE', 'README.md', 'README.en.md', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination (Join-Path $outputPath $document)
}
if (-not $FrameworkDependent) {
    $runtime = Get-Content -LiteralPath (Join-Path $outputPath 'PromptDC.runtimeconfig.json') -Raw | ConvertFrom-Json
    $packageLocation = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate runtime package licenses' }
    $packageRoot = ($packageLocation -split ':\s+', 2)[1].Trim()
    foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
        $packageName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $packagePath = Join-Path $packageRoot "$packageName/$($framework.version)"
        $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE(?:\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$' })
        if (-not @($notices | Where-Object { $_.Name -match '^LICENSE' }).Count) { throw "Missing runtime license: $packageName" }
        $noticeOutput = Join-Path $outputPath "licenses/$($framework.name)-$($framework.version)"
        New-Item -ItemType Directory -Path $noticeOutput -Force | Out-Null
        foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $noticeOutput }
    }
}

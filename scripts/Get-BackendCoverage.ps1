[CmdletBinding()]
param([string]$ResultsPath = (Join-Path $PSScriptRoot '../TestResults/final-coverage'))
$ErrorActionPreference = 'Stop'
$assemblies = @{}
# SIGER.Tests emits one report containing all four productive assemblies.
# Group by measured assembly, not by the project that ran the tests.
$backendAssemblies = @('SIGER.Domain', 'SIGER.Application', 'SIGER.Infrastructure', 'SIGER.API')
foreach ($file in Get-ChildItem -LiteralPath $ResultsPath -Recurse -Filter coverage.cobertura.xml) {
    [xml]$document = Get-Content -LiteralPath $file.FullName
    $sourceRoot = @($document.coverage.sources.source)[0]
    foreach ($package in $document.coverage.packages.package) {
        if ($package.name -notin $backendAssemblies) { continue }
        if (-not $assemblies.ContainsKey($package.name)) { $assemblies[$package.name] = @{} }
        foreach ($class in $package.classes.class) {
            foreach ($line in $class.lines.line) {
                $sourceFile = [IO.Path]::GetFullPath((Join-Path $sourceRoot $class.filename))
                $key = $sourceFile + ':' + $line.number
                $assemblies[$package.name][$key] = [Math]::Max([int]$assemblies[$package.name][$key], [int]$line.hits)
            }
        }
    }
}
foreach ($name in $assemblies.Keys | Sort-Object) {
    $lines = $assemblies[$name]
    $authored = @($lines.Keys | Where-Object { $_ -notmatch '[\\/]obj[\\/]' })
    $covered = @($lines.Values | Where-Object { $_ -gt 0 }).Count
    $authoredCovered = @($authored | Where-Object { $lines[$_] -gt 0 }).Count
    [pscustomobject]@{
        Assembly = $name
        UniqueLines = $lines.Count
        CoveredUniqueLines = $covered
        UnionLinePercent = [Math]::Round(100 * $covered / $lines.Count, 2)
        AuthoredLines = $authored.Count
        AuthoredCoveredLines = $authoredCovered
        AuthoredLinePercent = [Math]::Round(100 * $authoredCovered / $authored.Count, 2)
    }
}

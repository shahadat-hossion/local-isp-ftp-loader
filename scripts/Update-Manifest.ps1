[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Checksum,
    [Parameter(Mandatory)] [string] $Timestamp,
    [Parameter(Mandatory)] [string] $SourceUrl
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'manifest.json'
$metaPath = Join-Path $root 'Jellyfin.Plugin.LinkAdder\meta.json'
$manifest = @(Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json)
$meta = Get-Content -Raw -LiteralPath $metaPath | ConvertFrom-Json
$plugin = $manifest[0]
$entry = [ordered]@{
    version = $Version
    changelog = $meta.changelog
    targetAbi = $meta.targetAbi
    sourceUrl = $SourceUrl
    checksum = $Checksum.ToLowerInvariant()
    timestamp = $Timestamp
}

$olderVersions = @($plugin.versions | Where-Object { $_.version -ne $Version })
$plugin.versions = @([pscustomobject]$entry) + $olderVersions
$json = ConvertTo-Json -InputObject @($manifest) -Depth 10
Set-Content -LiteralPath $manifestPath -Value $json -Encoding utf8

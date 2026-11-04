[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectFile = Join-Path $projectRoot 'Jellyfin.Plugin.LinkAdder\Jellyfin.Plugin.LinkAdder.csproj'
$publishDirectory = Join-Path $projectRoot 'dist\LinkAdder'
$version = ([xml](Get-Content -Raw -LiteralPath $projectFile)).Project.PropertyGroup.Version
$packageVersion = $version -replace '\.0$', ''
$packagePath = Join-Path $projectRoot "dist\Sam-Online-FTP-Loader-$packageVersion.zip"

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

dotnet publish $projectFile -c Release -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'Jellyfin.Plugin.LinkAdder\meta.json') -Destination $publishDirectory -Force

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

$packageFiles = @(
    (Join-Path $publishDirectory 'Jellyfin.Plugin.LinkAdder.dll'),
    (Join-Path $publishDirectory 'Jellyfin.Plugin.LinkAdder.deps.json'),
    (Join-Path $publishDirectory 'meta.json')
)
Compress-Archive -LiteralPath $packageFiles -DestinationPath $packagePath -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $packagePath -Algorithm MD5
$checksumPath = "$packagePath.md5"
Set-Content -LiteralPath $checksumPath -Value ("{0}  {1}" -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $packagePath)) -Encoding ascii

Write-Host "Package: $packagePath"
Write-Host "MD5:     $($hash.Hash.ToLowerInvariant())"

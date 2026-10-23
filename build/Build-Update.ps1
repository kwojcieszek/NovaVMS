#Requires -Version 5.1
<#
.SYNOPSIS
Builds one framework-dependent AnyCPU update ZIP.
.EXAMPLE
.\build\Build-Update.ps1
.EXAMPLE
.\build\Build-Update.ps1 -Version 2026.10.24
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'out')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $PSBoundParameters.ContainsKey('Version')) {
    [xml]$buildProperties = Get-Content -LiteralPath (Join-Path $repository 'src\Directory.Build.props') -Raw
    $Version = $buildProperties.SelectSingleNode('/Project/PropertyGroup/ReleaseDate').InnerText
}
$releaseDate = [datetime]::MinValue
if (-not [datetime]::TryParseExact($Version, 'yyyy.MM.dd', [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None, [ref]$releaseDate)) {
    throw 'Version must be a valid date in yyyy.MM.dd format.'
}
Get-Command dotnet -ErrorAction Stop | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$project = Join-Path $repository 'src\NovaVMS.App\NovaVMS.App.csproj'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$scratch = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('NovaVMS-update-build-' + [guid]::NewGuid().ToString('N'))))
$packageName = 'novavms_' + $releaseDate.ToString('yyyy_MM_dd') + '.zip'
$temporaryPackage = Join-Path $scratch $packageName
$excludedDirectories = @('hcnetsdk', 'libvlc', 'runtimes', 'logs')
$excludedFiles = @('config.json', 'sync.json', 'local.json', '.novavms-update.lock')
$requiredFiles = @(
    'NovaVMS.exe', 'NovaVMS.dll', 'NovaVMS.deps.json', 'NovaVMS.runtimeconfig.json',
    'updater/NovaVMS.Updater.exe', 'updater/NovaVMS.Updater.dll',
    'updater/NovaVMS.Updater.deps.json', 'updater/NovaVMS.Updater.runtimeconfig.json'
)

try {
    [IO.Directory]::CreateDirectory($scratch) | Out-Null
    $included = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $zip = [IO.Compression.ZipFile]::Open($temporaryPackage, [IO.Compression.ZipArchiveMode]::Create)
    try {
            $publish = Join-Path $scratch 'publish'
            Write-Host "Publishing NovaVMS $Version (AnyCPU)..."
            & dotnet publish $project -c Release --self-contained false -p:PlatformTarget=AnyCPU `
                "-p:ReleaseDate=$Version" --artifacts-path (Join-Path $scratch 'artifacts') -o $publish
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }
            foreach ($required in $requiredFiles) {
                if (-not [IO.File]::Exists((Join-Path $publish $required))) { throw "Missing file: $required" }
            }
            $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $publish 'NovaVMS.dll')).Version
            if ($assemblyVersion.Major -ne $releaseDate.Year -or $assemblyVersion.Minor -ne $releaseDate.Month `
                    -or $assemblyVersion.Build -ne $releaseDate.Day) {
                throw 'The application version does not match the requested release date.'
            }
            $prefix = $publish.TrimEnd([char[]]@('\', '/')) + [IO.Path]::DirectorySeparatorChar
            foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse -Force | Sort-Object FullName) {
                $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/')
                $parts = $relative.Split('/')
                $directories = if ($parts.Length -gt 1) { $parts[0..($parts.Length - 2)] } else { @() }
                if (@($directories | Where-Object { $_ -in $excludedDirectories }).Count -gt 0 `
                        -or $file.Name -in $excludedFiles -or $file.Extension -eq '.pdb') { continue }
                $entryName = $relative
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName,
                    [IO.Compression.CompressionLevel]::Optimal) | Out-Null
                $included.Add($entryName) | Out-Null
            }
            foreach ($required in $requiredFiles) {
                if (-not $included.Contains($required)) { throw "The ZIP is missing $required." }
            }
    }
    finally { $zip.Dispose() }

    $hash = (Get-FileHash -LiteralPath $temporaryPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{ version = $Version; file = $packageName; sha256 = $hash } | ConvertTo-Json
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $package = Join-Path $output $packageName
    [IO.File]::Copy($temporaryPackage, $package, $true)
    $manifestPath = Join-Path $output 'latest.json'
    [IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
    Write-Host "AnyCPU update ZIP: $package"
    Write-Host "Manifest:          $manifestPath"
    Write-Host 'Single AnyCPU build; requires an installed .NET 10 runtime and matching native dependencies.'
    Write-Host 'Excluded directories: hcnetsdk, libvlc, runtimes (at every level).'
}
finally {
    # Only remove the unique temporary directory created by this invocation.
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char[]]@('\', '/')) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedScratch.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) `
            -and [IO.Path]::GetFileName($resolvedScratch).StartsWith('NovaVMS-update-build-', [StringComparison]::Ordinal) `
            -and (Test-Path -LiteralPath $resolvedScratch)) {
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}

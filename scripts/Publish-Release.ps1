param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{4}\.\d{2}\.\d{2}$')]
    [string]$Version,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$releaseDate = [DateTime]::ParseExact($Version, 'yyyy.MM.dd', [Globalization.CultureInfo]::InvariantCulture)
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts/releases' }
$releaseDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null
$zipName = 'novavms_' + $releaseDate.ToString('yyyy_MM_dd') + '.zip'
$zipPath = Join-Path $releaseDirectory $zipName
if (Test-Path -LiteralPath $zipPath) { throw "Release package already exists: $zipPath" }
$manifestPath = Join-Path $releaseDirectory 'latest.json'
if (Test-Path -LiteralPath $manifestPath) {
    $previous = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ([DateTime]::ParseExact($previous.version, 'yyyy.MM.dd', [Globalization.CultureInfo]::InvariantCulture) -gt $releaseDate) {
        throw 'The output directory already contains a newer release.'
    }
}
$publishDirectory = Join-Path $releaseDirectory ('work/' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
& dotnet publish (Join-Path $projectRoot 'src/NovaVMS.App/NovaVMS.App.csproj') -c Release -r $Runtime --self-contained true "-p:ReleaseDate=$Version" -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
foreach ($required in @('NovaVMS.exe', 'NovaVMS.dll', 'updater/NovaVMS.Updater.exe', 'updater/NovaVMS.Updater.runtimeconfig.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $required))) { throw "Release is missing $required" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$manifest = [ordered]@{ version = $Version; file = $zipName; sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() }
$temporaryManifest = Join-Path $releaseDirectory ('latest-' + [Guid]::NewGuid().ToString('N') + '.tmp')
[IO.File]::WriteAllText($temporaryManifest, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporaryManifest -Destination $manifestPath -Force
Write-Output "Ready to upload: $zipPath"
Write-Output "Manifest: $manifestPath"

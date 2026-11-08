param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'out/mobile'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage = Join-Path ([IO.Path]::GetTempPath()) ('NovaVMS-mobile-source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
try {
    foreach ($directory in @('src/NovaVMS.Mobile', 'src/NovaVMS.Mobile.Android', 'src/NovaVMS.Mobile.iOS', 'src/NovaVMS.Mobile.Tests', 'src/NovaVMS.Core', 'src/NovaVMS.Protocols')) {
        Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | ForEach-Object {
                $relative = [IO.Path]::GetRelativePath($root, $_.FullName)
                $target = Join-Path $stage $relative
                New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
                Copy-Item -LiteralPath $_.FullName -Destination $target
            }
    }
    foreach ($file in @('src/Directory.Build.props','build/build_mobile_ios.sh','build/Build-Mobile-Android.ps1')) {
        $target = Join-Path $stage $file
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
    }
    Copy-Item -LiteralPath (Join-Path $root 'src/NovaVMS.Mobile/README.md') -Destination (Join-Path $stage 'README.md')
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $zip = Join-Path $OutputDirectory 'novavms-mobile-source.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolvedStage.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        !(Split-Path $resolvedStage -Leaf).StartsWith('NovaVMS-mobile-source-')) { throw 'Unsafe staging path.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}

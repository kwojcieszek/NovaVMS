param(
    [string]$AndroidSdkDirectory = $env:ANDROID_HOME,
    [string]$JavaSdkDirectory = $env:JAVA_HOME,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'out/mobile'),
    [string]$KeyStore,
    [string]$KeyAlias,
    [string]$StorePasswordFile,
    [string]$KeyPasswordFile
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/NovaVMS.Mobile.Android/NovaVMS.Mobile.Android.csproj'
$configuration = 'Debug'
$arguments = @('publish', $project, '-c', $configuration, '-p:EmbedAssembliesIntoApk=true', '-p:AndroidPackageFormats=apk')
if ($KeyStore) {
    if (!$KeyAlias -or !$StorePasswordFile -or !$KeyPasswordFile) { throw 'Signing requires KeyAlias, StorePasswordFile and KeyPasswordFile.' }
    $configuration = 'Release'
    $arguments[3] = $configuration
    $arguments += @('-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$([IO.Path]::GetFullPath($KeyStore))",
        "-p:AndroidSigningKeyAlias=$KeyAlias", "-p:AndroidSigningStorePass=file:$([IO.Path]::GetFullPath($StorePasswordFile))",
        "-p:AndroidSigningKeyPass=file:$([IO.Path]::GetFullPath($KeyPasswordFile))")
}
if ($AndroidSdkDirectory) { $arguments += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
if ($JavaSdkDirectory) { $arguments += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Android build failed ($LASTEXITCODE)." }
$directory = Join-Path (Split-Path $project) "bin/$configuration/net10.0-android/publish"
$apk = Get-ChildItem -LiteralPath $directory -Filter '*-Signed.apk' | Select-Object -First 1
if (!$apk) { throw "Signed APK was not produced in $directory." }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$filename = if ($KeyStore) { 'novavms-android.apk' } else { 'novavms-android-test.apk' }
$destination = Join-Path $OutputDirectory $filename
Copy-Item -LiteralPath $apk.FullName -Destination $destination -Force
Get-FileHash -LiteralPath $destination -Algorithm SHA256
if (!$KeyStore) { Write-Host 'Test APK signed with the local Android debug key. Not a Play Store release.' }

param(
    [Parameter(Mandatory=$true)][string]$SigningKeyPath,
    [string]$OutputFolder,
    [string]$InstallerUrl,
    [string]$FeedUrl,
    [string]$RuntimeVersion='10.0.13',
    [string]$ReleaseNotes='Duck Studio update'
)
$ErrorActionPreference='Stop'
$sourceRoot=$PSScriptRoot
$editor=Join-Path $sourceRoot 'Editor\WeftStudio.csproj'
$installerProject=Join-Path $sourceRoot 'Installer\DuckStudio.Setup.csproj'
$scratch=Join-Path $sourceRoot '.build'
if(-not $OutputFolder){$OutputFolder=Join-Path (Split-Path -Parent $sourceRoot) 'DuckStudio-Windows'}
$OutputFolder=[IO.Path]::GetFullPath($OutputFolder)
if(-not (Test-Path -LiteralPath $SigningKeyPath)){throw 'The private release key is required. It is intentionally kept outside the source/delivery folders.'}
[xml]$project=Get-Content -LiteralPath $editor
$version=[string]$project.Project.PropertyGroup.Version
$env:DOTNET_CLI_HOME=Join-Path $scratch 'dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$appStage=Join-Path $scratch 'app'
$installerStage=Join-Path $scratch 'installer'
New-Item -ItemType Directory -Path $scratch -Force|Out-Null
$scratchFull=[IO.Path]::GetFullPath($scratch)
if((Get-Item -LiteralPath $scratchFull).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Build scratch folder must not be a junction or symbolic link.'}
foreach($stagePath in @($appStage,$installerStage)){
    $stageFull=[IO.Path]::GetFullPath($stagePath)
    if(-not $stageFull.StartsWith($scratchFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build stage escaped the scratch folder.'}
    if(Test-Path -LiteralPath $stageFull){
        if((Get-Item -LiteralPath $stageFull).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Build stage must not be a symbolic link.'}
        Remove-Item -LiteralPath $stageFull -Recurse -Force
    }
}
dotnet publish $editor -c Release -r win-x64 --self-contained true "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=true -p:NuGetAuditMode=all "-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905" --ignore-failed-sources -o $appStage
if($LASTEXITCODE -ne 0){throw 'Application publish failed.'}
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Vendor\Runtime') -File|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $appStage -Force}
$licenseStage=Join-Path $appStage 'third_party_licenses';New-Item -ItemType Directory -Path $licenseStage -Force|Out-Null
Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Vendor\Licenses')|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $licenseStage -Recurse -Force}
foreach($name in @('README.md','FEATURES-2.1.md','THIRD_PARTY.md','LICENSE')){Copy-Item -LiteralPath (Join-Path $sourceRoot "Editor\$name") -Destination $appStage -Force}
New-Item -ItemType Directory -Path (Join-Path $appStage 'Branding') -Force|Out-Null
foreach($name in @('AppIcon.ico','AppIconSource.png')){Copy-Item -LiteralPath (Join-Path $sourceRoot "Editor\Assets\$name") -Destination (Join-Path $appStage 'Branding') -Force}
$fileHashes=[ordered]@{}
Get-ChildItem -LiteralPath $appStage -File -Recurse|Where-Object{$_.Extension -ne '.pdb'}|ForEach-Object{$fileHashes[[IO.Path]::GetRelativePath($appStage,$_.FullName)]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
$payloadMetadata=Join-Path $scratch 'payload.json'
$payload=Join-Path $scratch 'payload.zip'
[IO.File]::WriteAllText($payloadMetadata,(@{Version=$version;Files=$fileHashes}|ConvertTo-Json -Depth 6))
if(Test-Path -LiteralPath $payload){Remove-Item -LiteralPath $payload}
Add-Type -AssemblyName System.IO.Compression
$archive=[IO.Compression.ZipFile]::Open($payload,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($relative in $fileHashes.Keys){
        $sourceFile=Join-Path $appStage $relative
        $entry=$archive.CreateEntry($relative.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
        $modified=[IO.File]::GetLastWriteTime($sourceFile)
        if($modified.Year -lt 1980){$modified=[DateTime]::new(1980,1,1)}
        if($modified.Year -gt 2107){$modified=[DateTime]::new(2107,12,31)}
        $entry.LastWriteTime=[DateTimeOffset]$modified
        $input=[IO.File]::OpenRead($sourceFile);$output=$entry.Open()
        try{$input.CopyTo($output)}finally{$output.Dispose();$input.Dispose()}
    }
}finally{$archive.Dispose()}
$brand=Join-Path $sourceRoot 'Editor\Assets'
dotnet publish $installerProject -c Release -r win-x64 --self-contained true "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=true -p:NuGetAuditMode=all "-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905" --ignore-failed-sources "-p:Version=$version" "-p:BrandingRoot=$brand" "-p:PayloadPath=$payload" "-p:PayloadMetadataPath=$payloadMetadata" -o $installerStage
if($LASTEXITCODE -ne 0){throw 'Installer publish failed.'}
New-Item -ItemType Directory -Path $OutputFolder -Force|Out-Null
Get-ChildItem -LiteralPath $appStage|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $OutputFolder -Recurse -Force}
$installerFolder=Join-Path $OutputFolder 'Installer';$feedFolder=Join-Path $OutputFolder 'UpdateFeed'
New-Item -ItemType Directory -Path $installerFolder,$feedFolder -Force|Out-Null
$setup=Join-Path $installerFolder 'DuckStudioSetup.exe'
Copy-Item -LiteralPath (Join-Path $installerStage 'DuckStudioSetup.exe') -Destination $setup -Force
if(-not $InstallerUrl){$InstallerUrl=([Uri]$setup).AbsoluteUri}
if(-not $FeedUrl){$FeedUrl=([Uri](Join-Path $feedFolder 'latest.json')).AbsoluteUri}
$rsa=[Security.Cryptography.RSA]::Create()
try{
    $rsa.ImportFromPem([IO.File]::ReadAllText($SigningKeyPath))
    $pinned=[IO.File]::ReadAllText((Join-Path $brand 'UpdatePublicKey.pem')).Trim()
    if($rsa.ExportSubjectPublicKeyInfoPem().Trim() -ne $pinned){throw 'Signing key does not match the public key pinned in Duck Studio.'}
    $release=[ordered]@{Version=$version;InstallerUrl=$InstallerUrl;Sha256=(Get-FileHash -LiteralPath $setup).Hash;Size=(Get-Item -LiteralPath $setup).Length;Notes=$ReleaseNotes;PublishedUtc=[DateTime]::UtcNow.ToString('o')}
    $bytes=[Text.Encoding]::UTF8.GetBytes(($release|ConvertTo-Json -Compress))
    $signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
    [IO.File]::WriteAllText((Join-Path $feedFolder 'latest.json'),(@{Payload=[Convert]::ToBase64String($bytes);Signature=[Convert]::ToBase64String($signature)}|ConvertTo-Json))
}finally{$rsa.Dispose()}
[IO.File]::WriteAllText((Join-Path $OutputFolder 'UPDATE.json'),(@{Version=$version;Notes=$ReleaseNotes;BuiltUtc=[DateTime]::UtcNow.ToString('o');RendererSha256=(Get-FileHash -LiteralPath (Join-Path $OutputFolder 'rage.exe')).Hash;ApplicationSha256=(Get-FileHash -LiteralPath (Join-Path $OutputFolder 'DuckStudio.dll')).Hash}|ConvertTo-Json))
[IO.File]::WriteAllText((Join-Path $OutputFolder 'update-channel.json'),(@{FeedUrl=$FeedUrl}|ConvertTo-Json))
[IO.File]::WriteAllText((Join-Path $OutputFolder 'duck-install.json'),(@{AppId='DuckStudio';Version=$version;Files=@($fileHashes.Keys)+@('update-channel.json');FeedUrl=$FeedUrl}|ConvertTo-Json -Depth 5))
Write-Output "Ready: $OutputFolder"
Write-Output 'Upload the installer and signed latest.json to publish an online release. The private key is never included.'

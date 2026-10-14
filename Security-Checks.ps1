param([string]$ScanFolder)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$tools=Join-Path $root 'work/security-tools/gitleaks-8.30.1'
New-Item -ItemType Directory -Path $tools -Force|Out-Null
$zip=Join-Path $tools 'gitleaks.zip'
if(-not (Test-Path -LiteralPath $zip)){
    Invoke-WebRequest 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $zip
}
if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne 'D29144DEFF3A68AA93CED33DDDF84B7FDC26070ADD4AA0F4513094C8332AFC4E'){
    throw 'Secret scanner download checksum does not match the pinned release.'
}
Expand-Archive -LiteralPath $zip -DestinationPath $tools -Force
$exe=Join-Path $tools 'gitleaks.exe'
if($ScanFolder){& $exe dir $ScanFolder --redact --no-banner}
else{& $exe git $root '--log-opts=--all' --redact --no-banner}
if($LASTEXITCODE -ne 0){throw 'Secret scan failed. Review the redacted findings before publishing.'}

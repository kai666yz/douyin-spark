$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$target=Join-Path $root 'runtime\MicrosoftEdgeWebview2Setup.exe'
$expected='13F6D9CDC8C3E7186E4B180DB7F48E199EE97D631480E8391FB3106F338FB21A'
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
if(-not(Test-Path -LiteralPath $target)){
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $target
}
if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected){throw 'Bootstrapper hash changed. Verify the new Microsoft signature and update the pinned hash in get_runtime.ps1 and RuntimeInstaller.cs before building.'}
$signature=Get-AuthenticodeSignature -LiteralPath $target
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){throw 'A valid Microsoft-signed WebView2 bootstrapper is required.'}
Write-Host 'Microsoft WebView2 bootstrapper signature and SHA256 verified. It is bundled but never executed during build.'

$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$version='1.0.4314.35'
$hash='90778DEB1B77E36292828B4B5833C9255C27533F40E0DDD55891AE718944EDC4'
$cache=Join-Path $root 'build'
$sdk=Join-Path $cache 'webview2-sdk'
$target=Join-Path $root 'vendor\webview2'
New-Item -ItemType Directory -Path $cache,$target -Force | Out-Null
$archive=Join-Path $cache ('webview2-'+$version+'.zip')
if(-not(Test-Path -LiteralPath $archive)){
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 $client=New-Object Net.WebClient
 try{$client.DownloadFile('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/'+$version+'/microsoft.web.webview2.'+$version+'.nupkg',$archive)}finally{$client.Dispose()}
}
if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $hash){throw 'WebView2 SDK checksum mismatch.'}
Expand-Archive -LiteralPath $archive -DestinationPath $sdk -Force
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath (Join-Path $sdk ('lib\net462\'+$name)) -Destination $target -Force}
Copy-Item -LiteralPath (Join-Path $sdk 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $sdk 'LICENSE.txt') -Destination (Join-Path $target 'WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $sdk 'NOTICE.txt') -Destination (Join-Path $target 'WebView2-NOTICE.txt') -Force
Write-Host ('WebView2 SDK '+$version+' verified.')

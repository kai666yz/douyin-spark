$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
& (Join-Path $projectDir 'tools\get_webview2.ps1')
& (Join-Path $projectDir 'tools\get_runtime.ps1')
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){Copy-Item -LiteralPath (Join-Path $projectDir ('vendor\webview2\'+$name)) -Destination $projectDir -Force}
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkDir 'csc.exe'))) {
    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Install Microsoft .NET Framework 4.x before building.'
}
$compilerArgs = @(
    '/nologo', '/target:winexe', '/platform:x64', '/codepage:65001',
    "/out:$projectDir\抖音续火花.exe",
    "/win32icon:$projectDir\icon.ico",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:System.Management.dll', '/r:System.Data.dll',
    "/r:$projectDir\Microsoft.Web.WebView2.Core.dll", "/r:$projectDir\Microsoft.Web.WebView2.WinForms.dll",
    "/r:$frameworkDir\Microsoft.VisualBasic.dll",
    "/r:$frameworkDir\WPF\WindowsFormsIntegration.dll"

)
$compilerArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Write-Host "Built: $projectDir\抖音续火花.exe"

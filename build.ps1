$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkDir 'csc.exe'))) {
    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Install Microsoft .NET Framework 4.x before building.'
}
$compilerArgs = @(
    '/nologo', '/target:winexe', '/codepage:65001',
    "/out:$projectDir\抖音续火花.exe",
    "/win32icon:$projectDir\icon.ico",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    "/r:$frameworkDir\WPF\WindowsFormsIntegration.dll",
    "$projectDir\src\app.cs"
)
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Write-Host "Built: $projectDir\抖音续火花.exe"

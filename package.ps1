$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
$python = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python)) {
    & (Join-Path $PSScriptRoot 'setup.ps1')
}
& $python -m pip install pyinstaller
if ($LASTEXITCODE -ne 0) { throw 'PyInstaller installation failed.' }
& $python -m PyInstaller --noconfirm --onedir --name spark_backend --collect-all playwright --copy-metadata playwright --hidden-import fetch_following --distpath (Join-Path $PSScriptRoot 'dist') --workpath (Join-Path $PSScriptRoot 'build\backend') --specpath (Join-Path $PSScriptRoot 'build') (Join-Path $PSScriptRoot 'spark_backend.py')
if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
& $python (Join-Path $PSScriptRoot 'tools\package_release.py')
if ($LASTEXITCODE -ne 0) { throw 'Release packaging failed.' }

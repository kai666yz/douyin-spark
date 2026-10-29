$ErrorActionPreference = 'Stop'
$venvPython = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3 -m venv (Join-Path $PSScriptRoot '.venv')
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python -m venv (Join-Path $PSScriptRoot '.venv')
    } else {
        throw 'Install Python 3.10+ from python.org and add it to PATH first.'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create Python environment.' }
}
& $venvPython -m pip install -r (Join-Path $PSScriptRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Failed to install Python dependencies.' }
$friendsFile = Join-Path $PSScriptRoot 'friends.json'
if (-not (Test-Path -LiteralPath $friendsFile)) {
    [System.IO.File]::WriteAllText($friendsFile, '[]', (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host 'Setup complete. Install Edge/Chrome/Quark, then run build.ps1.'

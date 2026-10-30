$ErrorActionPreference = 'Stop'
Write-Progress -Activity 'Douyin Spark source setup' -Status '1/4 Checking Python' -PercentComplete 10
$venvPython = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Progress -Activity 'Douyin Spark source setup' -Status '2/4 Creating virtual environment' -PercentComplete 30
    if (Get-Command py -ErrorAction SilentlyContinue) {
        & py -3 -m venv (Join-Path $PSScriptRoot '.venv')
    } elseif (Get-Command python -ErrorAction SilentlyContinue) {
        & python -m venv (Join-Path $PSScriptRoot '.venv')
    } else {
        throw 'Install Python 3.10+ from python.org and add it to PATH first.'
    }
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create Python environment.' }
}
Write-Progress -Activity 'Douyin Spark source setup' -Status '3/4 Installing Python components (requires network)' -PercentComplete 55
& $venvPython -m pip install -r (Join-Path $PSScriptRoot 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Failed to install Python dependencies.' }
Write-Progress -Activity 'Douyin Spark source setup' -Status '4/4 Checking browser dependencies; using installed Edge/Chrome/Quark' -PercentComplete 90
& $venvPython (Join-Path $PSScriptRoot 'douyin_spark.py') --environment-check
if ($LASTEXITCODE -ne 0) { throw 'Environment validation failed. Check the message above and retry setup.ps1.' }
$friendsFile = Join-Path $PSScriptRoot 'friends.json'
if (-not (Test-Path -LiteralPath $friendsFile)) {
    [System.IO.File]::WriteAllText($friendsFile, '[]', (New-Object System.Text.UTF8Encoding($false)))
}
Write-Progress -Activity 'Douyin Spark source setup' -Completed
Write-Host 'Setup complete. Browser kernels are not downloaded; use installed Edge/Chrome/Quark. Run build.ps1 next.'

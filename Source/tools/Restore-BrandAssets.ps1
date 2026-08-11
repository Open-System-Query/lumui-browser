$ErrorActionPreference = "Stop"
$browserRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $browserRoot "resources\branding\LUMUI_icon.ico.b64"
$target = Join-Path $browserRoot "LUMUI_icon.ico"
$encoded = (Get-Content -Raw -Path $source) -replace "\s", ""
[System.IO.File]::WriteAllBytes($target, [System.Convert]::FromBase64String($encoded))

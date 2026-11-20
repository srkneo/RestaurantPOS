# Screenshots for the user guide (Notion): sample data first, then every main page, saved as PNG files in
# E:\Projects\guide-screens (next to the repo, never committed). Uses the E2E Docker copy; it is removed afterwards.
#   powershell -ExecutionPolicy Bypass -File scripts\guide-shots.ps1
$env:SEED = '1'
$env:SHOTS = '1'
& "$PSScriptRoot/e2e-local.ps1" -Specs seed-data, zz-guide-shots
exit $LASTEXITCODE

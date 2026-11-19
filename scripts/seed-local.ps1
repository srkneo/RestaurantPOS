# Checks the sample-data script on the E2E Docker copy (the containers are removed afterwards).
$env:SEED = '1'
& "$PSScriptRoot/e2e-local.ps1" seed-data
exit $LASTEXITCODE

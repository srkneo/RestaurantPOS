# Runs the Playwright end-to-end tests on this PC against a throwaway copy of the whole stack.
# Own Docker project (pos-e2e) and port 18080, so it never touches the dev database or a shop-PC install.
#   powershell -ExecutionPolicy Bypass -File scripts\e2e-local.ps1                 # all tests
#   powershell -ExecutionPolicy Bypass -File scripts\e2e-local.ps1 menu-19         # only files matching "menu-19"
param([string[]]$Specs)
$ErrorActionPreference = "Continue"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$project = "pos-e2e"
$files = @("-f", "docker-compose.yml", "-f", "scripts/e2e-compose.override.yml")

docker compose -p $project @files down -v 2>&1 | Out-Null
docker compose -p $project @files up -d --build 2>&1 | Select-Object -Last 5
if ($LASTEXITCODE -ne 0) { Write-Host "E2E RESULT: FAILED (could not start the stack; is Docker running?)"; exit 1 }

$up = $false
for ($i = 0; $i -lt 100; $i++) {
  try { Invoke-WebRequest "http://localhost:18080/health" -UseBasicParsing -TimeoutSec 3 | Out-Null; $up = $true; break } catch { Start-Sleep -Seconds 3 }
}
if (-not $up) {
  docker compose -p $project @files logs --tail 60
  docker compose -p $project @files down -v 2>&1 | Out-Null
  Write-Host "E2E RESULT: FAILED (the app did not start)"
  exit 1
}

Set-Location (Join-Path $repo "e2e")
if (-not (Test-Path "node_modules")) { npm ci 2>&1 | Select-Object -Last 3 }
npx playwright install chromium 2>&1 | Select-Object -Last 2
$env:BASE_URL = "http://localhost:18080"
$out = Join-Path $env:TEMP "pos-e2e-results"
npx playwright test @Specs --reporter=list --output=$out
$code = $LASTEXITCODE

Set-Location $repo
if ($code -ne 0) { Write-Host "---- API log (last lines) ----"; docker compose -p $project @files logs api --tail 80 --no-color }
docker compose -p $project @files down -v 2>&1 | Out-Null
if ($code -eq 0) { Write-Host "E2E RESULT: PASSED" } else { Write-Host "E2E RESULT: FAILED" }
exit $code

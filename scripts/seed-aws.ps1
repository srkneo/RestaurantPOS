# Sample data on a live server, for trying the POS by hand: menu, staff, a busy day of orders, bills, tips,
# guest feedback and expenses. Asks for the owner's password (it is not saved anywhere).
#
#   powershell -ExecutionPolicy Bypass -File scripts\seed-aws.ps1 -Url https://your-server -Email owner@yourshop.com
#
# Running it again adds another batch of orders (the menu is reused). Staff password Test#1234, manager PIN 4321.
param([Parameter(Mandatory = $true)][string]$Url, [Parameter(Mandatory = $true)][string]$Email)
$ErrorActionPreference = 'Continue'
$secure = Read-Host "Password for $Email" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
$env:SEED = '1'; $env:BASE_URL = $Url.TrimEnd('/'); $env:E2E_EMAIL = $Email; $env:E2E_PASSWORD = $plain
$code = 1
Push-Location (Join-Path $PSScriptRoot '..\e2e')
try {
  if (-not (Test-Path node_modules)) { npm ci }
  npx playwright test seed-data --reporter=list --workers=1 --retries=0 --output="$env:TEMP\pos-seed"
  $code = $LASTEXITCODE
} finally {
  Pop-Location
  Remove-Item Env:E2E_PASSWORD -ErrorAction SilentlyContinue
}
if ($code -eq 0) { Write-Host "Sample data added. Open $Url and sign in." -ForegroundColor Green }
else { Write-Host "Something failed (see above)." -ForegroundColor Red }
exit $code

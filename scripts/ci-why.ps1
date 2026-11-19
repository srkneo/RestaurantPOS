# Shows why the GitHub checks of a branch failed: the latest runs and the failed steps' log.
# Used from a runner task so the output lands in aider-logs\<task>-try1.setup.txt.
#   powershell -ExecutionPolicy Bypass -File scripts\ci-why.ps1 fix/some-branch
param([string]$Branch)
$runs = gh run list --branch $Branch --limit 10 --json databaseId,workflowName,conclusion,status | ConvertFrom-Json
foreach ($r in $runs) { Write-Host "$($r.workflowName): $($r.status) $($r.conclusion) (run $($r.databaseId))" }
foreach ($r in ($runs | Where-Object { $_.conclusion -eq 'failure' } | Select-Object -First 2)) {
  Write-Host "==== $($r.workflowName) run $($r.databaseId): failed steps ===="
  gh run view $r.databaseId --log-failed 2>&1 | Select-Object -Last 150
}
exit 0

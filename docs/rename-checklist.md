# Rename checklist: RestaurantPOS to AllInOne POS (for Mahesh)

Do this yourself at a quiet time (no rush hour, the task queue empty, no release running). Screens, bills, texts and docs already say AllInOne POS (RN1 to RN3). This list covers what is still called RestaurantPOS. Suggested new names: repo `srkneo/AllInOnePOS`, folder `E:\Projects\AllInOnePOS`, images `ghcr.io/srkneo/allinonepos-api` and `ghcr.io/srkneo/allinonepos-web`. Pick the names once and use them everywhere below.

## 0. Before you start

- [ ] Stop the queue watcher and runner on the PC (Task Scheduler: disable the queue-watch task).
- [ ] Check GitHub Actions: no release or E2E run in progress, master is green.
- [ ] Note the current version on the cloud server and on each shop PC.

## 1. GitHub repo name

- [ ] GitHub > repo Settings > General > Repository name: change `RestaurantPOS` to the new name. Secrets, variables, branch protection, PRs and issues move with it. GitHub redirects the old URL, so shop PCs keep working meanwhile.
- [ ] Never create a new repo called `RestaurantPOS` afterwards: that breaks the redirect for every shop PC.
- [ ] On the PC: `cd E:\Projects\RestaurantPOS` then `git remote set-url origin https://github.com/srkneo/<new name>.git` and `git fetch` to check.
- [ ] Then, as a normal task (not by hand), change the repo name in: `deploy/auto-update.sh` (repo=, the commits API URL), `deploy/shop-install.sh` (REPO= and the curl line in the header), `.github/workflows/release.yml` (image source labels), docs and user guide links.

## 2. Docker image names

- [ ] Images today: `ghcr.io/srkneo/restaurantpos-api` and `ghcr.io/srkneo/restaurantpos-web`. They appear in `.github/workflows/release.yml` (API_IMAGE, WEB_IMAGE), `.github/workflows/deploy-check.yml`, `deploy/docker-compose.prod.yml`, `deploy/auto-update.sh`, `deploy/shop-install.sh`.
- [ ] Shop PCs pull with their own copy of the compose file and auto-update.sh. So for at least two releases, release.yml must push BOTH the old and the new names. Remove the old names only when every shop PC runs a version that uses the new names.
- [ ] After the first push of the new names: GitHub > Packages > each new package > Package settings: same visibility as the old one and linked to the repo.
- [ ] Cloud server: deploy.sh pulls with the new compose file; check `docker ps` shows the new image names.

## 3. Folder on the PC (E:\Projects)

- [ ] Rename `E:\Projects\RestaurantPOS` to the new folder name (close Visual Studio, VS Code and any running app first).
- [ ] Update the repo path in `E:\Projects\queue-watch.ps1` ($repo), `E:\Projects\release.ps1` (Set-Location), `E:\Projects\run-local.ps1` ($repo) and any path in `runner.ps1`, `runner2.ps1`, `scripts\overnight.ps1`.
- [ ] Update `gh ... -R srkneo/RestaurantPOS` in queue-watch.ps1 and the Actions link in release.ps1 to the new repo name.
- [ ] Task Scheduler: check the queue-watch task's command and start folder.
- [ ] Keep `Get-Process RestaurantPOS` in queue-watch.ps1 and run-local.ps1: the program file is still RestaurantPOS.dll (the project is not renamed).

## 4. GitHub Actions

- [ ] Workflows keep working after the repo rename; run CI and E2E once by hand on master.
- [ ] Notification texts in release.yml and uptime.yml (Telegram) and the deploy scripts (deploy.sh, backup.sh, bill-check.sh, restore-test.sh) still say RestaurantPOS: change them in a normal task.
- [ ] Required status checks in branch protection keep their job names; check the PR merge button still waits for them.

## 5. Do NOT rename

- Database names and the database user.
- OpenTelemetry service name and the Loki labels `restaurantpos-api` (Grafana alerts and dashboards depend on them).
- C# namespaces, `RestaurantPOS.csproj`, `RestaurantPOS.Tests`, `RestaurantPOS.slnx`.
- AWS resources in `infra/` (backup bucket `restaurantpos-backups-...`, budget `restaurantpos-monthly`): changing the bucket name in Terraform deletes and recreates the bucket.
- The server folder `~/pos`.
- deploy/RELEASE-NOTES-1.9.md (history).

## 6. After the rename

- [ ] Enable the queue-watch task again and let one small task run end to end (branch, PR, merge).
- [ ] Next release: check the cloud server and one shop PC update to it (auto-update log on the shop PC).
- [ ] Tell Claude the new names so AGENTS.md, README.md and the task runner notes are updated.

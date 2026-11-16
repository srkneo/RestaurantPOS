# Load test (batch Q2)

Many restaurants billing at the same time, then a check that every bill is right.
**Never run this against production.** It creates restaurants and thousands of bills.

## 1. Test server (staging), once
In AWS CloudShell, folder `~/RestaurantPOS/infra`:
```
git pull
tofu apply -var staging_enabled=true -var staging_bundle=small_3_2
tofu output staging_ip
tofu output staging_url
tofu output -raw staging_platform_password
tofu output platform_email
```
`small_3_2` is the same size as production, so the numbers mean something.
In GitHub > Settings > Environments > **staging**, set secrets `DEPLOY_HOST` (staging IP) and `SITE_URL` (staging URL).

## 2. Raise the safety limits on the test server only
Lightsail > staging server > Connect (browser SSH):
```
cd ~/pos
echo "RATE_LOGIN_PER_MINUTE=5000" >> .env
echo "RATE_REQUESTS_PER_MINUTE=1000000" >> .env
```
(All test tills sign in from one computer, which the normal login limit of 10 a minute would block.)

## 3. Put the current version on it
On your PC: `git tag v1.6.4-rc.1` then `git push origin v1.6.4-rc.1` (a tag with `-rc` goes to staging only).

## 4. Run k6 from CloudShell (same AWS region as the server, so the network does not distort the numbers)
```
cd ~ && curl -sL https://github.com/grafana/k6/releases/download/v0.54.0/k6-v0.54.0-linux-amd64.tar.gz | tar xz
cd ~/RestaurantPOS && git pull
~/k6-v0.54.0-linux-amd64/k6 run -e SCALE=0.2 -e RESTAURANTS=5 -e BASE_URL=<staging url> -e PLATFORM_EMAIL=<platform email> -e PLATFORM_PASSWORD=<staging platform password> loadtest/pos-load.js
```
That is a 6-minute trial with 5 restaurants. When it passes, run the real test (30 minutes, 50 restaurants x 5 tills, lunch rush at 10 tills):
```
~/k6-v0.54.0-linux-amd64/k6 run -e BASE_URL=... -e PLATFORM_EMAIL=... -e PLATFORM_PASSWORD=... loadtest/pos-load.js
```
Paste the summary at the end (the lines with a tick or cross, and `http_req_duration`).

## 5. Check every bill on the server
Lightsail > staging > Connect:
```
cd ~/pos
curl -sL https://raw.githubusercontent.com/srkneo/RestaurantPOS/master/loadtest/check.sql -o check.sql
docker compose --env-file .env --env-file version.env -f docker-compose.prod.yml exec -T db psql -U pos -d restaurant_pos < check.sql
```
Sections 1 to 9 must be empty. The summary shows how many bills were checked.

## 6. Capacity (once)
Run again with `-e RESTAURANTS=100`, then 150, ... until `http_req_duration p(95)` goes over 800 ms or errors appear.
The last passing number is how many restaurants one $12 server carries.

## 7. Afterwards
Delete the test server to stop paying for it: `tofu apply -var staging_enabled=false`.

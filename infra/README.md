# Servers (OpenTofu)

Creates in AWS Mumbai (ap-south-1):
- **pos-production**: Lightsail Ubuntu 24.04, 2 GB, static IP, firewall 22/80/443, Docker, swap, automatic security updates, and its `~/pos/.env`.
- **pos-staging** (only when `staging_enabled = true`): the same, 1 GB.
- **S3 bucket** for nightly backups (kept 30 days) and a key that can only add and read backups (no delete).
- **Budget e-mail** when the month's cost is heading over `monthly_budget_usd`.

Run it from **AWS CloudShell** (the `>_` icon at the top of the AWS console, region **Mumbai**). Nothing to install on your PC.
State is kept in an S3 bucket, so it can be run again later from CloudShell or anywhere.

## 1. Install OpenTofu in CloudShell (once)

```
mkdir -p ~/bin && cd ~/bin
curl -fsSLo tofu.zip https://github.com/opentofu/opentofu/releases/download/v1.10.0/tofu_1.10.0_linux_amd64.zip
unzip -o tofu.zip tofu && rm tofu.zip && cd ~
grep -q 'HOME/bin' ~/.bashrc || echo 'export PATH=$HOME/bin:$PATH' >> ~/.bashrc
export PATH=$HOME/bin:$PATH
tofu version
```

## 2. Code and state bucket (once)

```
git clone https://github.com/srkneo/RestaurantPOS.git
cd RestaurantPOS/infra
STATE=restaurantpos-tfstate-$(aws sts get-caller-identity --query Account --output text)
aws s3api create-bucket --bucket $STATE --region ap-south-1 --create-bucket-configuration LocationConstraint=ap-south-1
aws s3api put-bucket-versioning --bucket $STATE --versioning-configuration Status=Enabled
aws s3api put-public-access-block --bucket $STATE --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
```

## 3. Settings

```
cp terraform.tfvars.example terraform.tfvars
nano terraform.tfvars          # your e-mail; Ctrl+O, Enter, Ctrl+X
read -s TF_VAR_telegram_token  # paste the bot token, press Enter (nothing is shown)
export TF_VAR_telegram_token
```

## 4. Create

```
tofu init -backend-config="bucket=$STATE"
tofu plan -out plan            # read it: everything is "to add", nothing "to destroy"
tofu apply plan
tofu output
```

The server sets itself up in about 5 minutes after `apply` finishes.

## 5. Keep the passwords and the deploy key

```
tofu output -raw production_platform_password; echo     # your /platform sign-in: save it in a password manager
tofu output -raw deploy_private_key > ~/pos_deploy
```

CloudShell → **Actions → Download file** → `/home/cloudshell-user/pos_deploy`, then `rm ~/pos_deploy`.

## 6. GitHub secrets (PowerShell on your PC, in E:\Projects\RestaurantPOS)

Replace `<ip>` and `<url>` with `production_ip` and `production_url` from `tofu output`.

```
gh api -X PUT repos/srkneo/RestaurantPOS/environments/production
gh api -X PUT repos/srkneo/RestaurantPOS/environments/staging
cmd /c "gh secret set DEPLOY_SSH_KEY < %USERPROFILE%\Downloads\pos_deploy"
gh secret set DEPLOY_USER --body ubuntu
gh secret set DEPLOY_HOST --env production --body <ip>
gh secret set SITE_URL --env production --body <url>
gh secret set SITE_URL --body <url>
gh secret set POS_TG_TOKEN --body $env:POS_TG_TOKEN
gh secret set POS_TG_CHAT --body $env:POS_TG_CHAT
Remove-Item $HOME\Downloads\pos_deploy
```

## 7. First release

```
git checkout master; git pull
git tag v1.5.0
git push origin v1.5.0
```

Watch **Actions → Release**. When Telegram says ✅, open the URL and sign in with your e-mail and the production password.

## Later: turn on staging

Set `staging_enabled = true` in `terraform.tfvars`, run steps 4 and 5 (`staging_*` outputs), then:

```
gh secret set DEPLOY_HOST --env staging --body <staging ip>
gh secret set SITE_URL --env staging --body <staging url>
```

From then on: `v1.6.0-rc.1` goes to staging; when it is good, tag the same commit `v1.6.0` and the tested images go to production.

## Safety

- `pos-production` cannot be deleted by `tofu destroy` (protected), because the database is on it.
- Changing settings in `.env` later: edit `~/pos/.env` on the server (Lightsail → pos-production → Connect), then run `~/pos/deploy.sh <current version>`.

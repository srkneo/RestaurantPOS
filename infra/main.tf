# Production server (and optional staging) on Lightsail, plus an optional S3 bucket for nightly backups.

resource "random_id" "suffix" {
  byte_length = 3
}

# ---------- SSH key used by GitHub Actions to deploy (and by you, if needed) ----------

resource "tls_private_key" "deploy" {
  algorithm = "ED25519"
}

resource "aws_lightsail_key_pair" "deploy" {
  name       = "pos-deploy"
  public_key = tls_private_key.deploy.public_key_openssh
}

# ---------- Backups: S3 bucket, kept 30 days, and a key that can only add and read backups ----------

resource "aws_s3_bucket" "backups" {
  count  = var.backup_to_s3 ? 1 : 0
  bucket = "restaurantpos-backups-${random_id.suffix.hex}"
}

resource "aws_s3_bucket_public_access_block" "backups" {
  count  = var.backup_to_s3 ? 1 : 0
  bucket                  = aws_s3_bucket.backups[0].id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_lifecycle_configuration" "backups" {
  count  = var.backup_to_s3 ? 1 : 0
  bucket = aws_s3_bucket.backups[0].id

  rule {
    id     = "keep-nightly-30-days"
    status = "Enabled"
    filter {
      prefix = "nightly/"
    }
    expiration {
      days = 30
    }
  }
}

resource "aws_iam_user" "backup" {
  count = var.backup_to_s3 ? 1 : 0
  name = "pos-backup"
}

resource "aws_iam_user_policy" "backup" {
  count = var.backup_to_s3 ? 1 : 0
  name  = "pos-backup-write-read"
  user  = aws_iam_user.backup[0].name
  # No delete: a stolen server key cannot wipe the backups. Old ones expire by the lifecycle rule.
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect   = "Allow"
        Action   = ["s3:PutObject", "s3:GetObject"]
        Resource = "${aws_s3_bucket.backups[0].arn}/nightly/*"
      },
      {
        Effect   = "Allow"
        Action   = ["s3:ListBucket"]
        Resource = aws_s3_bucket.backups[0].arn
      }
    ]
  })
}

resource "aws_iam_access_key" "backup" {
  count = var.backup_to_s3 ? 1 : 0
  user  = aws_iam_user.backup[0].name
}

# ---------- Generated secrets for the servers' .env files ----------

resource "random_password" "db" {
  for_each = toset(["production", "staging"])
  length   = 32
  special  = false
}

# Meets the sign-in rules (upper, lower, digit, symbol) and avoids characters that .env files treat specially.
resource "random_password" "platform" {
  for_each         = toset(["production", "staging"])
  length           = 20
  min_upper        = 2
  min_lower        = 2
  min_numeric      = 2
  min_special      = 2
  override_special = "!#%*-_=+"
}

locals {
  firewall = [
    { protocol = "tcp", port = 22 },
    { protocol = "tcp", port = 80 },
    { protocol = "tcp", port = 443 },
    { protocol = "udp", port = 443 },
  ]

  production_site = "${replace(aws_lightsail_static_ip.production.ip_address, ".", "-")}.sslip.io"
  staging_site    = var.staging_enabled ? "${replace(aws_lightsail_static_ip.staging[0].ip_address, ".", "-")}.sslip.io" : ""

  production_env = templatefile("${path.module}/env.tftpl", {
    site_address      = local.production_site
    postgres_password = random_password.db["production"].result
    platform_email    = var.platform_email
    platform_password = random_password.platform["production"].result
    telegram_token    = var.telegram_token
    telegram_chat_id  = var.telegram_chat_id
    backup_bucket     = var.backup_to_s3 ? aws_s3_bucket.backups[0].bucket : ""
    backup_key_id     = var.backup_to_s3 ? aws_iam_access_key.backup[0].id : ""
    backup_key_secret = var.backup_to_s3 ? aws_iam_access_key.backup[0].secret : ""
    region            = var.region
  })

  # Staging keeps its backups on the server only.
  staging_env = var.staging_enabled ? templatefile("${path.module}/env.tftpl", {
    site_address      = local.staging_site
    postgres_password = random_password.db["staging"].result
    platform_email    = var.platform_email
    platform_password = random_password.platform["staging"].result
    telegram_token    = var.telegram_token
    telegram_chat_id  = var.telegram_chat_id
    backup_bucket     = ""
    backup_key_id     = ""
    backup_key_secret = ""
    region            = var.region
  }) : ""
}

# ---------- Production ----------

resource "aws_lightsail_static_ip" "production" {
  name = "pos-production-ip"
}

resource "aws_lightsail_instance" "production" {
  name              = "pos-production"
  availability_zone = var.availability_zone
  blueprint_id      = "ubuntu_24_04"
  bundle_id         = var.production_bundle
  key_pair_name     = aws_lightsail_key_pair.deploy.name
  user_data         = templatefile("${path.module}/cloud-init.sh.tftpl", { env_file = local.production_env, swap_gb = 2 })

  lifecycle {
    # The database lives on this server: never delete or replace it by accident.
    prevent_destroy = true
    # Setup runs once at creation; later .env changes are made on the server.
    ignore_changes = [user_data, key_pair_name, blueprint_id]
  }
}

resource "aws_lightsail_static_ip_attachment" "production" {
  static_ip_name = aws_lightsail_static_ip.production.name
  instance_name  = aws_lightsail_instance.production.name
}

resource "aws_lightsail_instance_public_ports" "production" {
  instance_name = aws_lightsail_instance.production.name

  dynamic "port_info" {
    for_each = local.firewall
    content {
      protocol  = port_info.value.protocol
      from_port = port_info.value.port
      to_port   = port_info.value.port
    }
  }
}

# ---------- Staging (optional) ----------

resource "aws_lightsail_static_ip" "staging" {
  count = var.staging_enabled ? 1 : 0
  name  = "pos-staging-ip"
}

resource "aws_lightsail_instance" "staging" {
  count             = var.staging_enabled ? 1 : 0
  name              = "pos-staging"
  availability_zone = var.availability_zone
  blueprint_id      = "ubuntu_24_04"
  bundle_id         = var.staging_bundle
  key_pair_name     = aws_lightsail_key_pair.deploy.name
  user_data         = templatefile("${path.module}/cloud-init.sh.tftpl", { env_file = local.staging_env, swap_gb = 2 })

  lifecycle {
    ignore_changes = [user_data, key_pair_name, blueprint_id]
  }
}

resource "aws_lightsail_static_ip_attachment" "staging" {
  count          = var.staging_enabled ? 1 : 0
  static_ip_name = aws_lightsail_static_ip.staging[0].name
  instance_name  = aws_lightsail_instance.staging[0].name
}

resource "aws_lightsail_instance_public_ports" "staging" {
  count         = var.staging_enabled ? 1 : 0
  instance_name = aws_lightsail_instance.staging[0].name

  dynamic "port_info" {
    for_each = local.firewall
    content {
      protocol  = port_info.value.protocol
      from_port = port_info.value.port
      to_port   = port_info.value.port
    }
  }
}

# ---------- Cost warning by e-mail (to platform_email) ----------

resource "aws_budgets_budget" "monthly" {
  name         = "restaurantpos-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.monthly_budget_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  # Count real usage even while the free credits pay for it, so the warning is honest.
  cost_types {
    include_credit = false
    include_refund = false
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 80
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED"
    subscriber_email_addresses = [var.platform_email]
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "ACTUAL"
    subscriber_email_addresses = [var.platform_email]
  }
}

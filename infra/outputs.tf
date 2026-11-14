output "production_url" {
  value = "https://${local.production_site}"
}

output "production_ip" {
  value = aws_lightsail_static_ip.production.ip_address
}

output "staging_url" {
  value = var.staging_enabled ? "https://${local.staging_site}" : "(staging is off)"
}

output "staging_ip" {
  value = var.staging_enabled ? aws_lightsail_static_ip.staging[0].ip_address : ""
}

output "backup_bucket" {
  value = var.backup_to_s3 ? aws_s3_bucket.backups[0].bucket : "(backups stay on the server)"
}

output "platform_email" {
  value = var.platform_email
}

# Read with: tofu output -raw production_platform_password
output "production_platform_password" {
  value     = random_password.platform["production"].result
  sensitive = true
}

output "staging_platform_password" {
  value     = var.staging_enabled ? random_password.platform["staging"].result : ""
  sensitive = true
}

# For the GitHub secret DEPLOY_SSH_KEY: tofu output -raw deploy_private_key > pos_deploy
output "deploy_private_key" {
  value     = tls_private_key.deploy.private_key_openssh
  sensitive = true
}

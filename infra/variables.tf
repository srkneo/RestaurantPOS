variable "region" {
  description = "AWS region. Sydney: the region this AWS account allows."
  type        = string
  default     = "ap-southeast-2"
}

variable "availability_zone" {
  type    = string
  default = "ap-southeast-2a"
}

variable "production_bundle" {
  description = "Lightsail size for production. small_3_0 = 2 GB RAM. List: aws lightsail get-bundles --query \"bundles[?isActive].bundleId\""
  type        = string
  default     = "small_3_0"
}

variable "staging_enabled" {
  description = "Create the staging server too (turn on once the first restaurant is live)."
  type        = bool
  default     = false
}

variable "staging_bundle" {
  description = "Lightsail size for staging. micro_3_0 = 1 GB RAM (swap is added)."
  type        = string
  default     = "micro_3_0"
}

variable "platform_email" {
  description = "Your POS owner sign-in (the /platform page)."
  type        = string
}

variable "telegram_token" {
  description = "Telegram bot token for error and backup alerts. Pass with TF_VAR_telegram_token, never in a file in git."
  type        = string
  default     = ""
  sensitive   = true
}

variable "telegram_chat_id" {
  type    = string
  default = ""
}

variable "monthly_budget_usd" {
  description = "E-mail warning when this month's AWS cost is forecast to pass 80% of this, and when it passes it (credits not counted)."
  type        = number
  default     = 25
}

variable "backup_to_s3" {
  description = "Also copy nightly backups to an S3 bucket. Off: backups stay on the server only (7 days)."
  type        = bool
  default     = false
}

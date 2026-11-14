terraform {
  required_version = ">= 1.10.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }

  # State is kept in this folder (terraform.tfstate, not in git). Download a copy after each apply.
  # Later it can move to S3 with: tofu init -migrate-state
  backend "local" {}
}

provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project = "RestaurantPOS"
    }
  }
}

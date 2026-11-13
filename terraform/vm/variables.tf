variable "auth" {
  description = "WorkloadIdentityFederation in CI. SecurityToken on the laptop."
  type        = string
  default     = "WorkloadIdentityFederation"

  validation {
    condition     = contains(["WorkloadIdentityFederation", "SecurityToken"], var.auth)
    error_message = "auth must be WorkloadIdentityFederation or SecurityToken."
  }
}

variable "config_file_profile" {
  description = "The ~/.oci/config profile that oci session authenticate wrote. Read only with SecurityToken."
  type        = string
  default     = "laneway"
}

variable "region" {
  type    = string
  default = "eu-frankfurt-1"
}

variable "ci_resource_type" {
  description = "The res_type the provider sends in the token exchange. Same value as in terraform/oci-bootstrap."
  type        = string
  default     = "laneway-terraform"
}

variable "compartment_id" {
  description = "The laneway compartment, from the oci-bootstrap compartment_id output."
  type        = string
}

variable "availability_domain" {
  description = "1, 2 or 3. The placeholder A1 held the free allowance in AD-2."
  type        = number
  default     = 2

  validation {
    condition     = contains([1, 2, 3], var.availability_domain)
    error_message = "availability_domain must be 1, 2 or 3."
  }
}

variable "image_id" {
  description = "Canonical-Ubuntu-24.04-aarch64-2026.09.18-0 in eu-frankfurt-1. A new value replaces the VM."
  type        = string
  default     = "ocid1.image.oc1.eu-frankfurt-1.aaaaaaaa4yruuzv353vx4cf3tcsimt5xqvls6reilpxccqhxbyfk6rzn57qq"
}

variable "backup_bucket_name" {
  description = "Same value as in terraform/oci-bootstrap, whose policy names it."
  type        = string
  default     = "laneway-backups"
}

variable "backup_age_recipient" {
  description = "The age public key the VM encrypts dumps to. Its private key lives in Doppler laneway-backup and the password manager, never on the VM."
  type        = string
  default     = null

  validation {
    condition     = var.backup_age_recipient == null || can(regex("^age1[02-9ac-hj-np-z]{58}$", var.backup_age_recipient))
    error_message = "backup_age_recipient must be an age X25519 public key, age1 followed by 58 characters."
  }
}

variable "laneway_db_doppler_token_secret_id" {
  description = "OCID of the Vault secret laneway-db-doppler-token. The operator creates it by hand."
  type        = string
  default     = null
}

variable "laneway_api_doppler_token_secret_id" {
  description = "OCID of the Vault secret laneway-api-doppler-token. The operator creates it by hand."
  type        = string
  default     = null
}

variable "tailscale_client_secret_secret_id" {
  description = "OCID of the Vault secret laneway-vm-tailscale-client-secret. The operator creates it by hand."
  type        = string
  default     = null
}

variable "bootstrap_oci_cli_digest" {
  description = "The linux/arm64 ghcr.io/oracle/oci-cli digest first boot uses to read the Tailscale secret. Bump it by hand only if that image breaks. A new value replaces the VM."
  type        = string
  default     = "sha256:46295a1de0d4bd5855c0402f5d01baf57d5b6f7acce0b87ad9e6dcfbbeafc21a"

  validation {
    condition     = can(regex("^sha256:[0-9a-f]{64}$", var.bootstrap_oci_cli_digest))
    error_message = "bootstrap_oci_cli_digest must look like sha256: followed by 64 hex digits."
  }
}

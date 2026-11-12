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

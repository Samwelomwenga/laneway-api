variable "tenancy_ocid" {
  type = string
}

variable "region" {
  description = "The tenancy's home region. IAM writes only work there."
  type        = string
  default     = "eu-frankfurt-1"
}

variable "config_file_profile" {
  description = "The ~/.oci/config profile that oci session authenticate wrote."
  type        = string
  default     = "laneway"
}

variable "repository" {
  type    = string
  default = "Samwelomwenga/laneway-api"
}

variable "ci_audience" {
  description = "The audience the Terraform jobs request on their GitHub OIDC token. terraform-vm.yml passes the same value."
  type        = string
  default     = "https://cloud.oracle.com"
}

variable "ci_resource_type" {
  description = "The res_type the provider sends in the token exchange. terraform/vm uses the same value."
  type        = string
  default     = "laneway-terraform"
}

variable "vault_secret_names" {
  description = "The Vault secrets the VM reads at boot. terraform/vm and the operator use the same names."
  type        = list(string)
  default = [
    "laneway-db-doppler-token",
    "laneway-api-doppler-token",
    "laneway-vm-tailscale-client-secret",
  ]
}

variable "backup_bucket_name" {
  description = "The bucket the VM writes dumps to. terraform/vm creates it with this name."
  type        = string
  default     = "laneway-backups"
}

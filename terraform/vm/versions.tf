terraform {
  required_version = "1.15.8"

  cloud {
    organization = "laneway"

    workspaces {
      name = "laneway-vm"
    }
  }

  required_providers {
    oci = {
      source  = "oracle/oci"
      version = "~> 9.13"
    }
  }
}

provider "oci" {
  auth                                = var.auth
  config_file_profile                 = var.auth == "SecurityToken" ? var.config_file_profile : null
  region                              = var.region
  token_exchange_requested_token_type = "urn:oci:token-type:oci-rpst"
  token_exchange_resource_type        = var.ci_resource_type
  token_exchange_rpst_exp             = "60"
}

terraform {
  required_version = "1.15.8"

  cloud {
    organization = "laneway"

    workspaces {
      name = "laneway-oci-bootstrap"
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
  auth                = "SecurityToken"
  config_file_profile = var.config_file_profile
  region              = var.region
}

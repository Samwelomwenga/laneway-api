terraform {
  required_version = ">= 1.9"

  required_providers {
    grafana = {
      source  = "grafana/grafana"
      version = "~> 4.51"
    }
  }
}

provider "grafana" {
  url                 = var.grafana_url
  auth                = var.grafana_auth
  oncall_access_token = var.oncall_access_token
}

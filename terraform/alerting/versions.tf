terraform {
  required_version = "1.15.8"

  cloud {
    organization = "laneway"

    workspaces {
      name = "laneway-alerting"
    }
  }

  required_providers {
    grafana = {
      source  = "grafana/grafana"
      version = "~> 4.51"
    }
  }
}

provider "grafana" {
  url  = var.grafana_url
  auth = var.grafana_auth
}

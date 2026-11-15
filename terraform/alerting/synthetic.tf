data "grafana_synthetic_monitoring_probes" "public" {}

resource "grafana_synthetic_monitoring_check" "ready" {
  job               = "laneway-api-ready"
  target            = "https://laneway-api.samwelomwenga.com/health/ready"
  enabled           = var.production_hosted
  frequency         = 120000
  timeout           = 5000
  alert_sensitivity = "none"
  probes = [
    data.grafana_synthetic_monitoring_probes.public.probes.London,
    data.grafana_synthetic_monitoring_probes.public.probes.NorthVirginia,
  ]

  settings {
    http {
      method                          = "GET"
      ip_version                      = "V4"
      fail_if_not_ssl                 = true
      valid_status_codes              = [200]
      fail_if_body_not_matches_regexp = ["^Healthy$"]
    }
  }
}

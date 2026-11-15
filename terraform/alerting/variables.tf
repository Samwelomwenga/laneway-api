variable "grafana_url" {
  type    = string
  default = "https://lanewayapi.grafana.net"
}

variable "grafana_auth" {
  description = "Service account token for the stack."
  type        = string
  sensitive   = true
}

variable "sm_url" {
  description = "Synthetic Monitoring API host from the plugin's apiHost setting."
  type        = string
  default     = "https://synthetic-monitoring-api-eu-west-2.grafana.net"
}

variable "sm_access_token" {
  description = "Synthetic Monitoring access token from the plugin's Config page."
  type        = string
  sensitive   = true
}

variable "operator_username" {
  description = "The operator's Grafana username, the one person on the rota."
  type        = string
}

variable "operator_email" {
  type      = string
  sensitive = true
}

variable "telegram_bot_token" {
  type      = string
  sensitive = true
}

variable "telegram_chat_id" {
  type      = string
  sensitive = true
}

variable "logs_datasource_uid" {
  type    = string
  default = "grafanacloud-logs"
}

variable "metrics_datasource_uid" {
  type    = string
  default = "grafanacloud-prom"
}

variable "production_hosted" {
  description = "Whether production runs somewhere. Until it does, the uptime check stays disabled and every rule that needs a running VM stays paused. Logging stopped and no backup in 26 hours would fire all day."
  type        = bool
  default     = false
}

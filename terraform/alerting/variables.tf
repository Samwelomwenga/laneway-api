variable "grafana_url" {
  type    = string
  default = "https://lanewayapi.grafana.net"
}

variable "grafana_auth" {
  description = "Service account token for the stack."
  type        = string
  sensitive   = true
}

variable "oncall_access_token" {
  description = "IRM API token. The provider manages personal notification rules only with one."
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
  description = "Whether production runs somewhere. Until it does, the logging stopped rule stays paused, or it would page all day."
  type        = bool
  default     = false
}

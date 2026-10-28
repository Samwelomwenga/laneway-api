data "grafana_oncall_user" "operator" {
  username = var.operator_username
}

resource "grafana_oncall_user_notification_rule" "push" {
  user_id  = data.grafana_oncall_user.operator.id
  position = 0
  type     = "notify_by_mobile_app"
}

resource "grafana_oncall_user_notification_rule" "wait" {
  user_id  = data.grafana_oncall_user.operator.id
  position = 1
  type     = "wait"
  duration = 900
}

resource "grafana_oncall_user_notification_rule" "email" {
  user_id  = data.grafana_oncall_user.operator.id
  position = 2
  type     = "notify_by_email"
}

resource "grafana_oncall_escalation_chain" "page" {
  name = "Laneway page"
}

resource "grafana_oncall_escalation" "operator" {
  escalation_chain_id = grafana_oncall_escalation_chain.page.id
  position            = 0
  type                = "notify_persons"
  persons_to_notify   = [data.grafana_oncall_user.operator.id]
}

resource "grafana_oncall_integration" "page" {
  name = "Laneway page"
  type = "grafana_alerting"

  default_route {
    escalation_chain_id = grafana_oncall_escalation_chain.page.id
  }
}

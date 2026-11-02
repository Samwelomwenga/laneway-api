data "grafana_oncall_user" "operator" {
  username = var.operator_username
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

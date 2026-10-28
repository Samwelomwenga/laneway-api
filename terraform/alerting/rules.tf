locals {
  playbooks = "https://github.com/Samwelomwenga/laneway-api/blob/main/docs/runbooks/alerts"

  logs    = "{service_name=\"laneway-api\", deployment_environment_name=\"production\"}"
  metrics = "deployment_environment_name=\"production\""

  groups = {
    "Every minute" = 60
    "Hourly"       = 3600
  }

  rules = [
    {
      group         = "Every minute"
      title         = "Sign-in failure ratio"
      playbook      = "sign-in-failure-ratio"
      tier          = "notify"
      summary       = "Over half of at least 20 sign-in attempts in 15 minutes failed on a wrong password or an unknown account."
      source        = "metrics"
      expr          = "sum(increase(laneway_auth_sign_in_attempts_total{${local.metrics}, result=~\"wrong_password|unknown_account\"}[15m])) / (sum(increase(laneway_auth_sign_in_attempts_total{${local.metrics}}[15m])) >= 20)"
      range         = 900
      fires_when    = { type = "gt", params = [0.5] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "Refresh token reused"
      playbook      = "refresh-token-reused"
      tier          = "page"
      summary       = "A rotated refresh token came back. Someone may hold a stolen token."
      source        = "metrics"
      expr          = "sum(increase(laneway_auth_refresh_total{${local.metrics}, result=\"reused\"}[5m]))"
      range         = 300
      fires_when    = { type = "gt", params = [0] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "Delivery failed at Error"
      playbook      = "delivery-failed"
      tier          = "page"
      summary       = "Brevo refused a delivery with 401, 402, or 403. No mail goes out until it's fixed."
      source        = "logs"
      expr          = "sum(count_over_time(${local.logs} | scope_name=\"Laneway.Message\" | event_name=\"DeliveryFailed\" | severity_text=\"Error\" [5m]))"
      range         = 300
      fires_when    = { type = "gt", params = [0] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "WebhookRejected run"
      playbook      = "webhook-rejected-run"
      tier          = "notify"
      summary       = "5 or more Brevo webhook calls were rejected in 15 minutes."
      source        = "logs"
      expr          = "sum(count_over_time(${local.logs} | scope_name=\"Laneway.Message\" | event_name=\"WebhookRejected\" [15m]))"
      range         = 900
      fires_when    = { type = "gt", params = [4] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "One clientIp burst"
      playbook      = "client-ip-burst"
      tier          = "notify"
      summary       = "One client IP caused 30 or more Laneway warnings in 10 minutes."
      source        = "logs"
      expr          = "sum by (clientIp) (count_over_time(${local.logs} | scope_name=~`Laneway[.].+` | severity_text=\"Warning\" | clientIp!=\"\" [10m]))"
      range         = 600
      fires_when    = { type = "gt", params = [29] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "UnexpectedError"
      playbook      = "unexpected-error"
      tier          = "page"
      summary       = "The API answered a 500."
      source        = "logs"
      expr          = "sum(count_over_time(${local.logs} | scope_name=\"Laneway.Api.ApiExceptionHandler\" | event_name=\"UnexpectedError\" [5m]))"
      range         = 300
      fires_when    = { type = "gt", params = [0] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "Unknown value or field"
      playbook      = "unknown-value-or-field"
      tier          = "notify"
      summary       = "A request sent a value or a field the API doesn't know. Our clients never do that."
      source        = "logs"
      expr          = "sum(count_over_time(${local.logs} | scope_name=\"Laneway.Request\" | event_name=\"RequestCompleted\" | errorCodes=~`.*\"(unknownValue|unknownField)\".*` [15m]))"
      range         = 900
      fires_when    = { type = "gt", params = [0] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "Id scan"
      playbook      = "id-scan"
      tier          = "notify"
      summary       = "20 or more requests in 10 minutes named ids that don't exist."
      source        = "metrics"
      expr          = "sum(increase(laneway_authorization_denials_unknown_target_total{${local.metrics}}[10m]))"
      range         = 600
      fires_when    = { type = "gt", params = [19] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "Admin granted"
      playbook      = "admin-granted"
      tier          = "notify"
      summary       = "Someone became an admin of a workspace or a board."
      source        = "logs"
      expr          = "sum(count_over_time(${local.logs} | scope_name=\"Laneway.Access\" | event_name=~\"MemberAdded|MemberRoleChanged\" | role=\"Admin\" [5m]))"
      range         = 300
      fires_when    = { type = "gt", params = [0] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "Made public"
      playbook      = "made-public"
      tier          = "notify"
      summary       = "A workspace or a board became public."
      source        = "logs"
      expr          = "sum(count_over_time(${local.logs} | scope_name=\"Laneway.Access\" | event_name=\"VisibilityChanged\" | visibility=\"Public\" [5m]))"
      range         = 300
      fires_when    = { type = "gt", params = [0] }
      no_data_state = "OK"
    },
    {
      group         = "Every minute"
      title         = "Logging stopped"
      playbook      = "logging-stopped"
      tier          = "page"
      summary       = "Production sent no runtime metrics for 10 minutes. The app or its exporter is down."
      source        = "metrics"
      expr          = "count(count_over_time(dotnet_process_cpu_count{${local.metrics}, job=\"laneway-api\"}[10m]))"
      range         = 600
      fires_when    = { type = "lt", params = [1] }
      no_data_state = "Alerting"
      paused        = !var.production_hosted
    },
    {
      group         = "Hourly"
      title         = "Log usage"
      playbook      = "log-usage"
      tier          = "notify"
      summary       = "The stack took in over 1.33 GB of logs in a day, a pace past 80% of the 50 GB monthly cap."
      source        = "logs"
      expr          = "sum(bytes_over_time({service_name=~\".+\"}[1d]))"
      range         = 86400
      fires_when    = { type = "gt", params = [1330000000] }
      no_data_state = "OK"
    },
  ]

  sources = {
    logs    = { uid = var.logs_datasource_uid, query = { queryType = "instant" } }
    metrics = { uid = var.metrics_datasource_uid, query = { instant = true, range = false } }
  }
}

resource "grafana_rule_group" "laneway" {
  for_each         = local.groups
  name             = each.key
  folder_uid       = grafana_folder.laneway.uid
  interval_seconds = each.value

  dynamic "rule" {
    for_each = [for rule in local.rules : rule if rule.group == each.key]
    content {
      name           = rule.value.title
      condition      = "C"
      no_data_state  = rule.value.no_data_state
      exec_err_state = "Error"
      is_paused      = try(rule.value.paused, false)
      labels         = { tier = rule.value.tier }
      annotations = {
        summary     = rule.value.summary
        runbook_url = "${local.playbooks}/${rule.value.playbook}.md"
      }

      data {
        ref_id         = "A"
        datasource_uid = local.sources[rule.value.source].uid
        relative_time_range {
          from = rule.value.range
          to   = 0
        }
        model = jsonencode(merge({ refId = "A", expr = rule.value.expr }, local.sources[rule.value.source].query))
      }

      data {
        ref_id         = "C"
        datasource_uid = "__expr__"
        relative_time_range {
          from = 0
          to   = 0
        }
        model = jsonencode({
          refId      = "C"
          type       = "threshold"
          expression = "A"
          conditions = [{ evaluator = rule.value.fires_when }]
        })
      }
    }
  }
}


resource "grafana_contact_point" "page" {
  name = "Laneway page"

  oncall {
    url = grafana_oncall_integration.page.link
  }

  telegram {
    token   = var.telegram_bot_token
    chat_id = var.telegram_chat_id
  }
}

resource "grafana_contact_point" "notify" {
  name = "Laneway notify"

  email {
    addresses = [var.operator_email]
  }

  telegram {
    token   = var.telegram_bot_token
    chat_id = var.telegram_chat_id
  }
}

resource "grafana_notification_policy" "laneway" {
  contact_point   = grafana_contact_point.notify.name
  group_by        = ["grafana_folder", "alertname"]
  group_wait      = "30s"
  group_interval  = "5m"
  repeat_interval = "4h"

  policy {
    matcher {
      label = "alertname"
      match = "="
      value = "DatasourceError"
    }
    contact_point = grafana_contact_point.notify.name
  }

  policy {
    matcher {
      label = "tier"
      match = "="
      value = "page"
    }
    contact_point = grafana_contact_point.page.name
  }

  policy {
    matcher {
      label = "tier"
      match = "="
      value = "notify"
    }
    contact_point = grafana_contact_point.notify.name
  }
}

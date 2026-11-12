data "oci_objectstorage_namespace" "tenancy" {}

locals {
  backup_retention_days = {
    daily     = 30
    monthly   = 365
    predeploy = 30
  }
}

resource "oci_objectstorage_bucket" "backups" {
  compartment_id = var.compartment_id
  namespace      = data.oci_objectstorage_namespace.tenancy.namespace
  name           = var.backup_bucket_name
  access_type    = "NoPublicAccess"
}

resource "oci_objectstorage_object_lifecycle_policy" "backups" {
  namespace = oci_objectstorage_bucket.backups.namespace
  bucket    = oci_objectstorage_bucket.backups.name

  dynamic "rules" {
    for_each = local.backup_retention_days

    content {
      name        = "delete-${rules.key}"
      action      = "DELETE"
      target      = "objects"
      is_enabled  = true
      time_amount = rules.value
      time_unit   = "DAYS"

      object_name_filter {
        inclusion_prefixes = ["${rules.key}/"]
      }
    }
  }
}

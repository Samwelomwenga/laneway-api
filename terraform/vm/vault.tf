resource "oci_kms_vault" "laneway" {
  compartment_id = var.compartment_id
  display_name   = "laneway"
  vault_type     = "DEFAULT"

  lifecycle {
    prevent_destroy = true
  }
}

resource "oci_kms_key" "secrets" {
  compartment_id      = var.compartment_id
  display_name        = "laneway-secrets"
  management_endpoint = oci_kms_vault.laneway.management_endpoint
  protection_mode     = "SOFTWARE"

  key_shape {
    algorithm = "AES"
    length    = 32
  }

  lifecycle {
    prevent_destroy = true
  }
}

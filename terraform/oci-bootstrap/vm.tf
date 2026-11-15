resource "oci_identity_dynamic_group" "vm" {
  compartment_id = var.tenancy_ocid
  name           = "laneway-vm"
  description    = "Every instance in the laneway compartment."
  matching_rule  = "ALL {instance.compartment.id = '${oci_identity_compartment.laneway.id}'}"
}

resource "oci_identity_policy" "vm_secrets" {
  compartment_id = var.tenancy_ocid
  name           = "laneway-vm-secrets"
  description    = "The VM reads its boot secrets from Vault and no other secret."
  statements = [
    "Allow dynamic-group ${oci_identity_dynamic_group.vm.name} to read secret-bundles in compartment ${local.compartment_name} where any {${join(", ", [for name in var.vault_secret_names : "target.secret.name = '${name}'"])}}",
  ]
}

resource "oci_identity_policy" "vm_backups" {
  compartment_id = var.tenancy_ocid
  name           = "laneway-vm-backups"
  description    = "The VM writes new dumps to the backup bucket. It can't read, overwrite, or delete them."
  statements = [
    "Allow dynamic-group ${oci_identity_dynamic_group.vm.name} to manage objects in compartment ${local.compartment_name} where all {target.bucket.name = '${var.backup_bucket_name}', any {request.permission = 'OBJECT_CREATE', request.permission = 'OBJECT_INSPECT'}}",
  ]
}

resource "oci_identity_policy" "object_storage_lifecycle" {
  compartment_id = var.tenancy_ocid
  name           = "laneway-object-storage-lifecycle"
  description    = "Object Storage deletes expired dumps under the backup bucket's lifecycle rules."
  statements = [
    "Allow service objectstorage-${var.region} to manage object-family in compartment ${local.compartment_name}",
  ]
}

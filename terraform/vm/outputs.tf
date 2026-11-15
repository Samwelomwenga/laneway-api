output "public_ip" {
  value = oci_core_public_ip.vm.ip_address
}

output "vault_id" {
  value = oci_kms_vault.laneway.id
}

output "secrets_key_id" {
  value = oci_kms_key.secrets.id
}

output "compartment_id" {
  value = oci_identity_compartment.laneway.id
}

output "token_exchange_domain_url" {
  value = local.domain_url
}

output "token_exchange_client_id" {
  value = oci_identity_domains_app.ci.name
}

output "token_exchange_client_secret" {
  value     = oci_identity_domains_app.ci.client_secret
  sensitive = true
}

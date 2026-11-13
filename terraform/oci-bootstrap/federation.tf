data "oci_identity_domains" "default" {
  compartment_id = var.tenancy_ocid
  display_name   = "Default"
}

locals {
  domain_url = data.oci_identity_domains.default.domains[0].url

  ci_claims = {
    repository       = var.repository
    ref              = "refs/heads/main"
    job_workflow_ref = "${var.repository}/.github/workflows/terraform.yml@refs/heads/main"
    aud              = var.ci_audience
  }
}

resource "oci_identity_domains_app" "ci" {
  idcs_endpoint   = local.domain_url
  schemas         = ["urn:ietf:params:scim:schemas:oracle:idcs:App"]
  display_name    = "laneway-terraform-ci"
  description     = "Exchanges GitHub Actions OIDC tokens for OCI session tokens. No other grant."
  is_oauth_client = true
  client_type     = "confidential"
  allowed_grants  = ["client_credentials"]
  active          = true

  based_on_template {
    value = "CustomWebAppTemplateId"
  }

  lifecycle {
    # OCI adds the OCITags extension schema to the app after create.
    ignore_changes = [schemas]
  }
}

resource "oci_identity_domains_identity_propagation_trust" "github_actions" {
  idcs_endpoint          = local.domain_url
  schemas                = ["urn:ietf:params:scim:schemas:oracle:idcs:IdentityPropagationTrust"]
  name                   = "laneway-github-actions"
  description            = "Accepts the Terraform jobs' GitHub OIDC token, on main only."
  type                   = "JWT"
  issuer                 = "https://token.actions.githubusercontent.com"
  public_key_endpoint    = "https://token.actions.githubusercontent.com/.well-known/jwks"
  subject_type           = "Resource"
  allow_impersonation    = true
  impersonating_resource = var.ci_resource_type
  oauth_clients          = [oci_identity_domains_app.ci.name]
  active                 = true

  dynamic "claim_validations" {
    for_each = local.ci_claims

    content {
      name  = claim_validations.key
      value = claim_validations.value
    }
  }
}

resource "oci_identity_policy" "ci" {
  compartment_id = var.tenancy_ocid
  name           = "laneway-terraform-ci"
  description    = "terraform/vm in GitHub Actions manages everything in the laneway compartment and nothing else."
  statements = [
    "Allow any-user to manage all-resources in compartment ${local.compartment_name} where request.principal.type = 'identityfederateddomainapp'",
  ]
}

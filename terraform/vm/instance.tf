data "oci_identity_availability_domains" "all" {
  compartment_id = var.compartment_id
}

locals {
  availability_domain = data.oci_identity_availability_domains.all.availability_domains[var.availability_domain - 1].name
  data_volume_device  = "/dev/oracleoci/oraclevdb"

  vault_secret_ids = {
    laneway_db_doppler_token  = var.laneway_db_doppler_token_secret_id
    laneway_api_doppler_token = var.laneway_api_doppler_token_secret_id
    tailscale_client_secret   = var.tailscale_client_secret_secret_id
  }

  user_data = base64gzip(templatefile("${path.module}/cloud-init.yaml", {
    vault_secret_ids         = local.vault_secret_ids
    data_volume_device       = local.data_volume_device
    region                   = var.region
    object_storage_namespace = data.oci_objectstorage_namespace.tenancy.namespace
    backup_bucket_name       = oci_objectstorage_bucket.backups.name
    backup_age_recipient     = var.backup_age_recipient
    bootstrap_oci_cli_digest = var.bootstrap_oci_cli_digest
    compose                  = file("${path.module}/compose.yaml")
    postgres_initdb          = file("${path.module}/postgres-initdb.sh")
    laneway_secrets          = file("${path.module}/laneway-secrets.sh")
    laneway_deploy           = file("${path.module}/laneway-deploy.sh")
    laneway_backup           = file("${path.module}/laneway-backup.sh")
    alloy_config             = file("${path.module}/config.alloy")
  }))
}

resource "oci_core_instance" "vm" {
  compartment_id      = var.compartment_id
  availability_domain = local.availability_domain
  display_name        = "laneway"
  shape               = "VM.Standard.A1.Flex"

  shape_config {
    ocpus         = 2
    memory_in_gbs = 12
  }

  source_details {
    source_type             = "image"
    source_id               = var.image_id
    boot_volume_size_in_gbs = 50
  }

  create_vnic_details {
    subnet_id        = oci_core_subnet.public.id
    assign_public_ip = false
    hostname_label   = "laneway"
  }

  instance_options {
    are_legacy_imds_endpoints_disabled = true
  }

  metadata = {
    user_data = local.user_data
  }

  lifecycle {
    precondition {
      condition     = alltrue([for id in values(local.vault_secret_ids) : id != null])
      error_message = "Create the three Vault secrets and set their OCIDs before the full apply. A later change to user_data replaces the VM."
    }

    precondition {
      condition     = length(local.user_data) <= 32000
      error_message = "OCI caps instance metadata at 32,000 bytes, and the gzipped user_data is ${length(local.user_data)}."
    }

    precondition {
      condition     = var.backup_age_recipient != null
      error_message = "Set backup_age_recipient to the age public key before the full apply. A later change to user_data replaces the VM."
    }
  }
}

data "oci_core_vnic_attachments" "vm" {
  compartment_id = var.compartment_id
  instance_id    = oci_core_instance.vm.id
}

data "oci_core_private_ips" "vm" {
  vnic_id = data.oci_core_vnic_attachments.vm.vnic_attachments[0].vnic_id
}

resource "oci_core_public_ip" "vm" {
  compartment_id = var.compartment_id
  display_name   = "laneway"
  lifetime       = "RESERVED"
  private_ip_id  = data.oci_core_private_ips.vm.private_ips[0].id

  lifecycle {
    prevent_destroy = true
  }
}

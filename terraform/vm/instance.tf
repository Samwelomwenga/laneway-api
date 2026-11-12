data "oci_identity_availability_domains" "all" {
  compartment_id = var.compartment_id
}

locals {
  availability_domain = data.oci_identity_availability_domains.all.availability_domains[var.availability_domain - 1].name

  vault_secret_ids = {
    laneway_db_doppler_token  = var.laneway_db_doppler_token_secret_id
    laneway_api_doppler_token = var.laneway_api_doppler_token_secret_id
    tailscale_client_secret   = var.tailscale_client_secret_secret_id
  }
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
    user_data = base64encode(templatefile("${path.module}/cloud-init.yaml", {
      vault_secret_ids = local.vault_secret_ids
    }))
  }

  lifecycle {
    precondition {
      condition     = alltrue([for id in values(local.vault_secret_ids) : id != null])
      error_message = "Create the three Vault secrets and set their OCIDs before the full apply. A later change to user_data replaces the VM."
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

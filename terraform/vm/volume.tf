resource "oci_core_volume" "data" {
  compartment_id      = var.compartment_id
  availability_domain = local.availability_domain
  display_name        = "laneway-data"
  size_in_gbs         = 100
  vpus_per_gb         = 10

  # Created after the instance, so an out of capacity launch strands no volume in the wrong AD.
  depends_on = [oci_core_instance.vm]

  lifecycle {
    prevent_destroy = true
  }
}

resource "oci_core_volume_attachment" "data" {
  attachment_type = "paravirtualized"
  instance_id     = oci_core_instance.vm.id
  volume_id       = oci_core_volume.data.id
  device          = local.data_volume_device
  display_name    = "laneway-data"
}

resource "oci_identity_compartment" "laneway" {
  compartment_id = var.tenancy_ocid
  name           = "laneway"
  description    = "Everything the Laneway VM runs on."
}

locals {
  compartment_name = oci_identity_compartment.laneway.name
}

resource "oci_limits_quota" "always_free_shapes" {
  compartment_id = var.tenancy_ocid
  name           = "laneway-always-free-shapes"
  description    = "Zero every compute shape in the laneway compartment except the Always Free A1 allowance."
  statements = [
    "zero compute-core quotas in compartment ${local.compartment_name}",
    "zero compute-memory quotas in compartment ${local.compartment_name}",
    "set compute-core quota standard-a1-core-count to 2 in compartment ${local.compartment_name}",
    "set compute-memory quota standard-a1-memory-count to 12 in compartment ${local.compartment_name}",
    "set compute-core quota standard-a1-core-regional-count to 2 in compartment ${local.compartment_name}",
    "set compute-memory quota standard-a1-memory-regional-count to 12 in compartment ${local.compartment_name}",
  ]
}

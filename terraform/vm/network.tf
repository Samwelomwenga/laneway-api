resource "oci_core_vcn" "laneway" {
  compartment_id = var.compartment_id
  display_name   = "laneway"
  cidr_blocks    = ["10.0.0.0/16"]
  dns_label      = "laneway"
}

resource "oci_core_internet_gateway" "laneway" {
  compartment_id = var.compartment_id
  vcn_id         = oci_core_vcn.laneway.id
  display_name   = "laneway"
}

resource "oci_core_default_route_table" "laneway" {
  manage_default_resource_id = oci_core_vcn.laneway.default_route_table_id
  display_name               = "laneway"

  route_rules {
    destination       = "0.0.0.0/0"
    destination_type  = "CIDR_BLOCK"
    network_entity_id = oci_core_internet_gateway.laneway.id
  }
}

resource "oci_core_default_security_list" "laneway" {
  manage_default_resource_id = oci_core_vcn.laneway.default_security_list_id
  display_name               = "laneway"

  egress_security_rules {
    destination = "0.0.0.0/0"
    protocol    = "all"
  }

  ingress_security_rules {
    source   = "0.0.0.0/0"
    protocol = "1"

    icmp_options {
      type = 3
      code = 4
    }
  }

  ingress_security_rules {
    source   = oci_core_vcn.laneway.cidr_blocks[0]
    protocol = "1"

    icmp_options {
      type = 3
    }
  }
}

resource "oci_core_subnet" "public" {
  compartment_id             = var.compartment_id
  vcn_id                     = oci_core_vcn.laneway.id
  display_name               = "laneway-public"
  cidr_block                 = "10.0.0.0/24"
  dns_label                  = "public"
  route_table_id             = oci_core_default_route_table.laneway.id
  security_list_ids          = [oci_core_default_security_list.laneway.id]
  prohibit_public_ip_on_vnic = false
}

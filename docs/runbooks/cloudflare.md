# Cloudflare

Cloudflare does two jobs for Laneway. A remote-managed Tunnel carries every request from the edge to the API on the VM, and an R2 bucket holds the second copy of each backup. This page covers setting up both, the edge settings on the `samwelomwenga.com` zone, and rotating their tokens. [The VM runbook](vm.md) covers the VM itself. [The backup runbook](backup.md) covers what goes in the bucket.

## The Tunnel

The VM has no inbound port. `cloudflared` runs on it as the `cloudflared` Compose service, dials out to Cloudflare, and forwards `laneway-api.samwelomwenga.com` to `http://api:8080` on the Compose network. TLS ends at Cloudflare's edge on the free Universal SSL certificate, so nothing on the VM issues or renews certificates.

The Tunnel is remote-managed. Its routes live in the dashboard, and the VM holds only its token. A route change needs no change on the VM and never replaces it.

1. In the Cloudflare dashboard, open Zero Trust, then Networks, Tunnels. Click Create a tunnel and pick Cloudflared.
2. Name it `laneway`.
3. On the install page, pick Docker. The command it shows ends in `--token <token>`. Copy the token alone. Skip the install, since the VM runs the connector.
4. In the Doppler dashboard, project `laneway-api`, config `prd`, add it as `TUNNEL_TOKEN`. `laneway-secrets` writes it alone into `cloudflared.env` and keeps it out of `api.env`.
5. Open the tunnel's Routes tab and click Add published application:

   | Field | Value |
   |---|---|
   | Subdomain | `laneway-api` |
   | Domain | `samwelomwenga.com` |
   | Path | empty |
   | Service URL | `http://api:8080` |

   The scheme in the Service URL is the service type. Keep it `http://`, not the `https://` the placeholder shows. The API serves plain HTTP on the Compose network, so an `https://` URL makes `cloudflared` attempt TLS to Kestrel and every request fails.

   Leave every other setting at its default. Cloudflare creates a proxied CNAME for the host in the zone's DNS. If the save fails because a record exists, delete the old `laneway-api` record in DNS and save again.

The Tunnel shows Inactive until the first deploy starts `cloudflared`. Then it shows Healthy, and `https://laneway-api.samwelomwenga.com/health/ready` answers `Healthy`.

If the UI moves onto the VM later, it gets its own public hostname on this same Tunnel, such as `laneway-app` to `http://ui:<port>`. That's a dashboard change. The Compose service it points at is the only VM change.

## Edge settings

Set these by hand on the `samwelomwenga.com` zone. Most of them are zone-wide, so they apply to every other host on the zone too. Nothing here is in Terraform.

| Where | Setting | Value | Why |
|---|---|---|---|
| SSL/TLS, Edge Certificates | Always Use HTTPS | on | the API refuses plain HTTP anyway |
| SSL/TLS, Edge Certificates | Minimum TLS Version | TLS 1.2 | |
| SSL/TLS, Edge Certificates | TLS 1.3 | on | |
| SSL/TLS, Edge Certificates | HTTP Strict Transport Security | off | the API sends its own HSTS header |
| Security, Bots | Bot Fight Mode | off | it challenges Brevo's webhook and API clients, which can't solve a challenge |
| Security, WAF, Managed rules | Cloudflare Free Managed Ruleset | as it ships | |
| Security, WAF, Rate limiting rules | none | | the API rate-limits per client IP itself |
| Caching, Cache Rules | `laneway-api bypass`: Hostname equals `laneway-api.samwelomwenga.com` | Bypass cache | no API response is safe to share between clients |
| Speed, Optimization | compression | as it ships | the edge compresses, the API doesn't |
| Network | timeouts and maximum upload size | as they ship | Kestrel's 11 MB cap is the only size limit |

The API trusts `X-Forwarded-For` only from `cloudflared`'s static address on the Compose network, set in `terraform/vm/compose.yaml`. Cloudflare appends the client IP it saw, and the API takes the rightmost entry, so a client can't spoof its IP.

## R2

R2 holds the second copy of every dump. R2 needs a card on the Cloudflare account, even on the free allowance.

### The bucket

1. In the dashboard, open R2 Object Storage. Create a bucket named `laneway-backups` with an automatic location.
2. In the bucket's Settings, under Object lifecycle rules, add three rules. Leave the default rule that aborts incomplete multipart uploads.

   | Rule name | Prefix | Action |
   |---|---|---|
   | `daily` | `daily/` | delete objects 30 days after upload |
   | `monthly` | `monthly/` | delete objects 365 days after upload |
   | `predeploy` | `predeploy/` | delete objects 30 days after upload |

3. Leave Public access off. Leave CORS empty.

### The token

1. On the R2 overview page, open Manage API tokens and create an Account API token named `laneway-backup`.
2. Give it Object Read & Write. Apply it to `laneway-backups` alone. Set its TTL to the shared rotation day in [the Terraform runbook](terraform.md#rotate-the-tokens-every-90-days).
3. Cloudflare shows the access key ID, the secret access key, and the S3 endpoint once. In the Doppler dashboard, project `laneway-db`, config `prd`, add:

   ```sh
   R2_ACCESS_KEY_ID=<access key ID>
   R2_SECRET_ACCESS_KEY=<secret access key>
   R2_ENDPOINT=https://<account id>.r2.cloudflarestorage.com
   R2_BUCKET=laneway-backups
   ```

`laneway-secrets` writes every `R2_` key in `laneway-db` `prd` to `backup.env` and nothing else, so keep the prefix. The VM's nightly job writes with this token, and the monthly restore reads with it. The VM's OCI policy lets it create and list objects only. This token can also delete, so it never leaves `laneway-db` and the VM's `backup.env`.

Check it from the laptop. Before the first deploy it lists nothing. After it, `predeploy/` shows up, then `daily/` after the first nightly, and `monthly/` only after a run on the 1st:

```sh
doppler run --project laneway-db --config prd -- sh -c '
  export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare RCLONE_CONFIG_R2_REGION=auto
  export RCLONE_CONFIG_R2_ENDPOINT="$R2_ENDPOINT"
  export RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID"
  export RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY"
  rclone --config /dev/null lsf "r2:$R2_BUCKET"'
```

## Rotating the tokens

Both tokens get rotated on the shared 90-day rotation day in [the Terraform runbook](terraform.md#rotate-the-tokens-every-90-days). The VM keeps using the old ones until a deploy renders the new ones. The R2 token's TTL ends on that day, so redeploy before it does, or the next backup fails `r2-upload-failed`. The Vault token rotation redeploys on the same day, so one redeploy covers all of them.

### The Tunnel token

1. In Zero Trust, Networks, Tunnels, open `laneway` and click Refresh token. Copy the new token. The running connector keeps its connection until it restarts.
2. Set it as `TUNNEL_TOKEN` in `laneway-api` `prd`.
3. Redeploy the live digests on the VM, as in [the VM runbook](vm.md#the-two-doppler-tokens). `cloudflared.env` changes, so Compose recreates `cloudflared` with the new token. The edge answers 502 for a few seconds while it reconnects.
4. Check the Tunnel shows Healthy.

### The R2 token

1. Create a new token as in [The token](#the-token).
2. Set the new `R2_ACCESS_KEY_ID` and `R2_SECRET_ACCESS_KEY` in `laneway-db` `prd`.
3. Redeploy the live digests on the VM, so `backup.env` holds the new pair. A deploy takes a predeploy dump, so check `journalctl -t laneway-backup --since -10min` shows `result=ok`.
4. Delete the old token.

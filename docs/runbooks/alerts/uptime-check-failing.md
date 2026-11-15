# Uptime check failing

Tier: page. The threshold is a starting guess. Retune it once real traffic exists.

## What fired

Under half the Synthetic Monitoring runs against `https://laneway-api.samwelomwenga.com/health/ready` passed in the last 10 minutes. London and North Virginia each probe every 120 seconds, so the window holds about 10 runs. A run passes on a 200 with the body `Healthy` within 5 seconds, over TLS and IPv4. `/health/ready` answers `Healthy` only when the API reaches Postgres within 2 seconds. So this fires when the Tunnel is down, the API is down, or the API can't reach Postgres. One probe region failing alone keeps the ratio near half and doesn't fire. A deploy that rolls back inside a few minutes doesn't either. The check turns off and the rule pauses whenever `production_hosted` is false in the Terraform.

## The first query to run

In `grafanacloud-lanewayapi-prom`, the success ratio for each probe:

```promql
sum by (probe) (increase(probe_all_success_sum{job="laneway-api-ready"}[10m])) / sum by (probe) (increase(probe_all_success_count{job="laneway-api-ready"}[10m]))
```

Both probes near 0 means the API is down for everyone. One probe near 0 and the other near 1 means a probe region or the path from it is broken. In Testing & synthetics, then Synthetics, the `laneway-api-ready` check's logs show each failed run's status and body.

## Telling noise from attack

Noise looks like a deploy that took longer than usual, a Cloudflare incident, or a Grafana probe outage. Check the Cloudflare and Grafana status pages.

An attack looks like the endpoint answering with a body or a certificate that isn't ours, or Cloudflare showing a second connector on the `laneway` Tunnel. Someone holding `TUNNEL_TOKEN` can run their own `cloudflared` and take traffic for the hostname. A flood that keeps the API too busy to answer looks the same as an outage from outside, so check Cloudflare's traffic graph for the hostname.

## What to do

1. In the Cloudflare dashboard, open Zero Trust, then Networks, Tunnels. `laneway` must show Healthy with one connector. If it shows Down or Inactive, `cloudflared` on the VM isn't connected.
2. Over Tailscale SSH on `laneway`, compare the two health endpoints:

   ```sh
   curl -sS http://127.0.0.1:8080/health/live
   curl -sS http://127.0.0.1:8080/health/ready
   ```

   If `live` doesn't answer, the API is down. If `live` answers and `ready` doesn't, the API can't reach Postgres. If both answer `Healthy`, the fault is between the VM and the probes, so go back to step 1.
3. Check the Postgres container with `docker ps` and `docker logs --tail 100 laneway-postgres-1`. A stopped or restarting container, or a full disk in `df -h /var/lib/laneway`, explains a failing `ready`.
4. Check whether a deploy just rolled back:

   ```sh
   journalctl -t laneway-deploy --since "1 hour ago"
   ```

   `result=rolled-back` means the new images failed the health check and the old ones are back. The alert clears once runs pass again. `result=rollback-failed` means the old images failed the health check too. Read `docker logs --tail 100 laneway-api-1` and `journalctl -t laneway-deploy-output --since "1 hour ago"` for the reason.

## When to escalate

Escalate to an incident when the endpoint answers with content that isn't ours, the Tunnel shows a connector you didn't start, or `TUNNEL_TOKEN` changed in Doppler without a planned rotation.

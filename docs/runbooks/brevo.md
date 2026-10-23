# Brevo

Laneway sends email through Brevo's transactional API. Brevo calls back to `POST /api/v1/webhooks/brevo` when an address hard bounces, complains of spam, gets blocked, or unsubscribes. This page covers the dev account, the webhook token, and the Production setup.

The API refuses to start without `BREVO__APIKEY`, `BREVO__SENDEREMAIL`, and `BREVO__WEBHOOKSIGNINGKEY`. Run it through `doppler run --`. Tests don't need any of them, because `BrevoStub` stands in for Brevo.

## The dev account

Local runs send real mail through one shared dev Brevo account. It's separate from Production's, so a local bounce never lands on Production's blocklist.

- Sender: `no-reply@dev.laneway.samwelomwenga.com`, display name `Laneway (dev)`. Brevo has authenticated the `dev.laneway` subdomain, and its DNS records live in Cloudflare.
- IP blocking is off, since every developer sends from a different IP.
- The API key is named `laneway-api-dev`.
- No webhook is registered. Brevo can't reach `localhost`.
- The free plan sends 300 emails a day, shared by everyone on the account. `GET https://api.brevo.com/v3/account` shows what's left.

Doppler's `laneway-api` project, config `dev`, holds the three `BREVO__` variables. Mail goes to real inboxes, so sign up with your own address. Plus-addresses such as `you+signup1@gmail.com` give you as many test users as you need.

To try the webhook locally, use [`BrevoWebhook.http`](../../BrevoWebhook.http). It posts one Brevo-shaped event per handled type. Brevo can't reach `localhost`, so these requests stand in for it.

1. Mint a token from the dev key, as in [mint a token](#mint-a-token), and paste it into `@webhookToken`.
2. Send yourself a message, such as a change-password notice.
3. Read its `message-id` and address in psql, and paste them into `@messageId` and `@email`:

   ```sql
   SELECT "ProviderMessageId", "Address" FROM "Deliveries" WHERE "Status" = 'Sent' ORDER BY "CreatedAt" DESC LIMIT 1;
   ```

4. Send a request. It answers 204, and the API logs `AddressSuppressed`. Later mail to that address drops.

## The webhook token

Brevo doesn't sign webhook calls. It sends a static `Authorization` header, so the header holds a JWT signed with `BREVO__WEBHOOKSIGNINGKEY`. That key is never the access-token key, so neither kind of token passes as the other. The token's audience is `brevo-webhook`, only HS256 passes, and it lasts a year. ADR 0004 records this as its 13.2.1 exception.

### Mint a token

```sh
doppler run --config <dev or prd> -- dotnet run --no-launch-profile -- mint-brevo-webhook-token
```

The command prints the token and exits without starting the API. Run it from the repo root.

### Rotate the token

Mint a new one before the old one's year runs out:

1. Mint a token with the command above.
2. In Brevo's transactional webhook settings, edit Laneway's webhook.
3. Replace the bearer with the new token and save.

The old token keeps working until it expires, so nothing drops during the swap. Don't wait for the expiry. Brevo doesn't retry a 401, so every event between the expiry and the swap is lost.

A run of `WebhookRejected` warnings in the logs means the token in Brevo is wrong or expired. `reason: expired` means it's time to rotate.

### If a token leaks

Change the key. Every token minted from the old key stops working at once.

1. Make a new key with `openssl rand -base64 32`. The API needs at least 32 bytes.
2. Set it as `BREVO__WEBHOOKSIGNINGKEY` in the Doppler config, and restart the API.
3. Mint a token and paste it into Brevo, as in [rotate the token](#rotate-the-token).

A leaked token can only suppress addresses Laneway has already emailed, because an event must carry the `message-id` of a delivery to that address.

## Production

Only a real deployment needs these steps. In the existing Brevo account:

1. Authenticate a Production subdomain, such as `laneway.samwelomwenga.com` unless Laneway has its own domain by then. Add `no-reply@` on it with the display name `Laneway`.
2. Keep IP blocking on, and allow the host's outbound IP.
3. Create an API key named `laneway-api-prd`. Store it in Doppler's `prd` config as `BREVO__APIKEY`, with `BREVO__SENDEREMAIL` beside it. Never put either in `ci`, which syncs every secret to GitHub.
4. Add a fresh `BREVO__WEBHOOKSIGNINGKEY` to `prd` with `openssl rand -base64 32`. Never copy dev's.
5. Once the API has a public URL, add a transactional webhook pointing at `https://<api host>/api/v1/webhooks/brevo`. Tick hard bounce, spam, blocked, and unsubscribed. Nothing else. Set its bearer to a token minted from the `prd` config.
6. Set `WebApp:BaseUrl` and `Cors:AllowedOrigins` to the real web app origin.
7. Check the plan's sending limit. 300 a day won't cover Production for long.

An event the webhook doesn't handle logs `WebhookEventIgnored`. If those show up, someone ticked an extra box in step 5.

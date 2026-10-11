# Laneway API

## CI

CI runs no tests, so run `dotnet test` before you push. The suite needs Docker for Testcontainers and the local Supabase stack from `supabase start`.

[docs/ci.md](docs/ci.md) lists each check and how to fix it.

## Signing keys

The app won't start without at least one access token signing key. Doppler holds them as an ordered list, `AUTH__SIGNINGKEYS__{n}__ID` and `AUTH__SIGNINGKEYS__{n}__KEY`. Key 0 signs, and every listed key validates. The id is a year and month like `2026-10`. The key is 32 random bytes in base64:

```sh
openssl rand -base64 32
```

To rotate a key without signing anyone out, add the new key at position 1 and deploy. Then swap it to position 0 and deploy. Remove the old key 16 minutes later and deploy again.

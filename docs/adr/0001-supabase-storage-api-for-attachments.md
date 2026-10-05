# Supabase Storage API for attachment files

Attachment files live in a private Supabase Storage bucket. The API talks to it through Supabase's own Storage API with the `Supabase.Storage` package, not through Supabase's S3-compatible endpoint with `AWSSDK.S3`. The S3 route would have made a later move to R2 or AWS a config change. We chose Supabase's own client and its `DownloadOptions.FileName` on signed URLs, and accepted that the storage code only works with Supabase.

## Consequences

- `Upload` takes a `byte[]`, not a stream, so the API holds each file in memory up to the 10 MB cap before it uploads.
- Moving to another provider means rewriting the storage class and copying every object, not changing an endpoint URL.

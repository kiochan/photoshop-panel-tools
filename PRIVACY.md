# Publication review

Only reviewed source under src/, cep/, shared/, scripts/ and .github/, plus allowlisted root documents, is uploaded. Run `npm run privacy:check` before every upload, then manually review the entire staged file list and diff.

The checker scans allowlisted files and tracked paths for private keys, access tokens, embedded credentials and personal home paths. It does not prove that an arbitrary file is safe to publish. Do not commit references, chat material, artwork, recovery records, local settings, logs, private history, build output or unrelated plugin code. `.gitignore` does not untrack existing files.

The only server key in source is the verified public host key. Publisher private keys belong in GitHub Actions Secrets. Any review examples must use clearly synthetic data.

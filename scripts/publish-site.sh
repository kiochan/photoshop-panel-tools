#!/usr/bin/env bash
set -Eeuo pipefail

case "${TOOL_PRODUCT:-}" in panel-tools) ;; *) echo 'Unknown product.'; exit 1;; esac
case "${RELEASE_CHANNEL:-}" in dev|prod) ;; *) echo 'Unknown channel.'; exit 1;; esac
[[ "$RELEASE_HOST" =~ ^[a-zA-Z0-9.-]+$ ]]
[[ "$GITHUB_SHA" =~ ^[a-f0-9]{40}$ ]]
[[ "$GITHUB_RUN_ID" =~ ^[0-9]+$ && "$GITHUB_RUN_ATTEMPT" =~ ^[0-9]+$ ]]

bundle="artifacts/site-release-$TOOL_PRODUCT.tar.gz"
identity="$RUNNER_TEMP/panel-publish"
error_log="$RUNNER_TEMP/panel-publication-error.log"

# The server serializes a channel catalogue. Independent tool jobs may briefly
# contend for that lock; retry only the pre-commit lock failure, not bad packages.
for attempt in 1 2 3 4 5; do
  if ssh -T -i "$identity/id_ed25519" \
      -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes \
      -o UserKnownHostsFile="$identity/known_hosts" \
      "deploy@$RELEASE_HOST" \
      "publish $RELEASE_CHANNEL $GITHUB_SHA-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT" \
      < "$bundle" 2>"$error_log"; then
    exit 0
  fi
  if grep -Fq '[Errno 11]' "$error_log" && [ "$attempt" -lt 5 ]; then
    echo 'Another tool is switching this channel; retrying the short publication lock.'
    sleep "$attempt"
  else
    cat "$error_log" >&2
    exit 1
  fi
done
exit 1

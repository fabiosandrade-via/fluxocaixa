#!/bin/bash
set -euo pipefail

key_file="$(mktemp /tmp/fluxocaixa-mongo-key.XXXXXX)"
openssl rand -base64 756 > "$key_file"
chown mongodb:mongodb "$key_file"
chmod 400 "$key_file"

exec /usr/local/bin/docker-entrypoint.sh mongod \
  --replSet rs0 \
  --bind_ip_all \
  --keyFile "$key_file"

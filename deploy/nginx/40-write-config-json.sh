#!/bin/sh
# Writes the SPA runtime config so one image serves every environment (web/src/config.ts).
set -eu
api_base="${OQTISH_PUBLIC_API_BASE_URL:-/api}"
case "$api_base" in
  *\"*|*\\*) echo "OQTISH_PUBLIC_API_BASE_URL contains invalid characters" >&2; exit 1 ;;
esac
mkdir -p /tmp/oqtish
printf '{"apiBaseUrl":"%s"}\n' "$api_base" > /tmp/oqtish/config.json

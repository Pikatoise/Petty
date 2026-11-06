#!/bin/sh
set -eu
umask 077
cd "$(dirname "$0")"
if [ -e secrets ]; then
    echo "Refusing to replace existing secrets." >&2
    exit 1
fi
mkdir -m 700 secrets
mkdir -m 755 secrets/api secrets/migrations
openssl rand -hex 32 > secrets/db-owner-password
openssl rand -hex 32 > secrets/db-app-password
openssl rand -base64 48 > secrets/api/Auth__SigningKey
owner_password=$(cat secrets/db-owner-password)
app_password=$(cat secrets/db-app-password)
printf 'Host=db;Database=petty;Username=petty_app;Password=%s;GSS Encryption Mode=Disable;Maximum Pool Size=20;Timeout=5;Command Timeout=15\n' "$app_password" > secrets/api/ConnectionStrings__Petty
printf 'Host=db;Database=petty;Username=petty_owner;Password=%s;GSS Encryption Mode=Disable;Maximum Pool Size=5;Timeout=5;Command Timeout=60\n' "$owner_password" > secrets/migrations/ConnectionStrings__Petty
# Parent directory stays private on the host; mounted files are readable by the non-root API.
chmod 444 secrets/db-owner-password secrets/db-app-password secrets/api/* secrets/migrations/*
echo "Created private deployment secrets. Back up the signing key securely."

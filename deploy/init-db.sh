#!/bin/sh
set -eu
psql --set=ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    --set=app_password="$(cat /run/secrets/db-app-password)" <<'SQL'
CREATE ROLE petty_app LOGIN PASSWORD :'app_password';
GRANT CONNECT ON DATABASE petty TO petty_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO petty_app;
SQL

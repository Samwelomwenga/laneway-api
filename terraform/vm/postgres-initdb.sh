#!/bin/sh
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v db="$POSTGRES_DB" -v migrator_password="$MIGRATOR_PASSWORD" -v app_password="$APP_PASSWORD" <<'EOSQL'
CREATE ROLE laneway_migrator LOGIN PASSWORD :'migrator_password';
CREATE ROLE laneway_app LOGIN PASSWORD :'app_password';
GRANT CONNECT ON DATABASE :"db" TO laneway_migrator, laneway_app;
GRANT USAGE, CREATE ON SCHEMA public TO laneway_migrator;
GRANT USAGE ON SCHEMA public TO laneway_app;

SET ROLE laneway_migrator;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO laneway_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO laneway_app;
RESET ROLE;
EOSQL

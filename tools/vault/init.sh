#!/bin/sh
set -e -u

: "${VAULT_ADDR:?VAULT_ADDR is required}"
: "${VAULT_TOKEN:?VAULT_TOKEN is required}"
: "${RESTAURANTES_POSTGRES_PASSWORD:?RESTAURANTES_POSTGRES_PASSWORD is required}"
: "${RESTAURANTES_RABBITMQ_PASSWORD:?RESTAURANTES_RABBITMQ_PASSWORD is required}"

umask 077
mkdir -p /vault/auth

vault auth enable approle >/dev/null 2>&1 || true

vault policy write restaurantes-local - <<'POLICY'
path "secret/data/restaurantes/local" {
  capabilities = ["read"]
}
POLICY

vault write auth/approle/role/restaurantes-local \
  token_policies="restaurantes-local" \
  token_ttl="1h" \
  token_max_ttl="4h" \
  secret_id_ttl="0" \
  secret_id_num_uses="0" >/dev/null

vault read -field=role_id auth/approle/role/restaurantes-local/role-id \
  > /vault/auth/role-id
vault write -f -field=secret_id auth/approle/role/restaurantes-local/secret-id \
  > /vault/auth/secret-id
chmod 0444 /vault/auth/role-id /vault/auth/secret-id

signing_key="$(dd if=/dev/urandom bs=48 count=1 2>/dev/null | base64 | tr -d '\n')"

vault kv put secret/restaurantes/local \
  "ConnectionStrings__CashRegisterWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=cash_register;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__CatalogRead=Host=localhost;Port=5433;Database=restaurants_analytics;Search Path=catalog_read;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__CatalogWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=catalog_write;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__DiningWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=dining;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__IdentityWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=identity;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__OrdersRead=Host=localhost;Port=5433;Database=restaurants_analytics;Search Path=orders_read;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__OrdersWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=orders_write;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__PaymentsWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=payments;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__ReportingRead=Host=localhost;Port=5433;Database=restaurants_analytics;Search Path=reporting;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__RestaurantOperationsRead=Host=localhost;Port=5433;Database=restaurants_analytics;Search Path=restaurant_operations_read;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__RestaurantOperationsWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=restaurant_operations;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__SalesRead=Host=localhost;Port=5433;Database=restaurants_analytics;Search Path=sales_read;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  "ConnectionStrings__SalesWrite=Host=localhost;Port=5432;Database=restaurants_operational;Search Path=sales_write;Username=restaurants;Password=${RESTAURANTES_POSTGRES_PASSWORD}" \
  RabbitMq__UserName=restaurants \
  "RabbitMq__Password=${RESTAURANTES_RABBITMQ_PASSWORD}" \
  "Security__SigningKey=${signing_key}" >/dev/null

echo "Vault local secrets are ready."

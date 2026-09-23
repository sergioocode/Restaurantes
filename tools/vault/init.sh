#!/bin/sh
set -eu

: "${VAULT_ADDR:?VAULT_ADDR is required}"
: "${VAULT_TOKEN:?VAULT_TOKEN is required}"

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
  "ConnectionStrings__CashRegisterWrite=Host=localhost;Port=5432;Database=cash_register_write;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__CatalogRead=Host=localhost;Port=5432;Database=catalog_read;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__CatalogWrite=Host=localhost;Port=5432;Database=catalog_write;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__DiningWrite=Host=localhost;Port=5432;Database=dining_write;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__IdentityWrite=Host=localhost;Port=5432;Database=identity_write;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__OrdersRead=Host=localhost;Port=5432;Database=orders_read;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__OrdersWrite=Host=localhost;Port=5432;Database=orders_write;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__PaymentsWrite=Host=localhost;Port=5432;Database=payments_write;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__ReportingRead=Host=localhost;Port=5432;Database=reporting_read;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__RestaurantOperationsRead=Host=localhost;Port=5432;Database=restaurant_operations_read;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__RestaurantOperationsWrite=Host=localhost;Port=5432;Database=restaurant_operations_write;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__SalesRead=Host=localhost;Port=5432;Database=sales_read;Username=restaurants;Password=restaurants_dev" \
  "ConnectionStrings__SalesWrite=Host=localhost;Port=5432;Database=sales_write;Username=restaurants;Password=restaurants_dev" \
  RabbitMq__UserName=restaurants \
  RabbitMq__Password=restaurants_dev \
  "Security__SigningKey=${signing_key}" >/dev/null

echo "Vault local secrets are ready."

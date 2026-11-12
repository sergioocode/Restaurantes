#!/bin/sh
set -eu

: "${KEYCLOAK_ADMIN_USERNAME:?KEYCLOAK_ADMIN_USERNAME is required}"
: "${KEYCLOAK_ADMIN_PASSWORD:?KEYCLOAK_ADMIN_PASSWORD is required}"
: "${IDENTITY_KEYCLOAK_CLIENT_ID:?IDENTITY_KEYCLOAK_CLIENT_ID is required}"
: "${IDENTITY_KEYCLOAK_CLIENT_SECRET:?IDENTITY_KEYCLOAK_CLIENT_SECRET is required}"
: "${IDENTITY_KEYCLOAK_TEST_PASSWORD:?IDENTITY_KEYCLOAK_TEST_PASSWORD is required}"

kcadm=/opt/keycloak/bin/kcadm.sh
attempt=0
until "$kcadm" config credentials \
  --server http://keycloak:8080 \
  --realm master \
  --user "$KEYCLOAK_ADMIN_USERNAME" \
  --password "$KEYCLOAK_ADMIN_PASSWORD" >/dev/null 2>&1; do
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 60 ]; then
	echo "Keycloak did not become ready for admin configuration." >&2
	exit 1
  fi
  sleep 2
done

realm=restaurantes
if ! "$kcadm" get "realms/$realm" >/dev/null 2>&1; then
  echo "Imported Keycloak realm '$realm' is missing." >&2
  exit 1
fi

client_id=$(
  "$kcadm" get clients -r "$realm" -q "clientId=$IDENTITY_KEYCLOAK_CLIENT_ID" \
	| sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
	| head -n 1
)
if [ -n "$client_id" ]; then
  client_resource="clients/$client_id"
  client_operation=update
else
  client_resource=clients
  client_operation=create
fi
"$kcadm" "$client_operation" "$client_resource" -r "$realm" \
  -s "clientId=$IDENTITY_KEYCLOAK_CLIENT_ID" \
  -s enabled=true \
  -s protocol=openid-connect \
  -s publicClient=false \
  -s standardFlowEnabled=true \
  -s implicitFlowEnabled=false \
  -s directAccessGrantsEnabled=false \
  -s serviceAccountsEnabled=false \
  -s secret="$IDENTITY_KEYCLOAK_CLIENT_SECRET" \
  -s 'redirectUris=["http://localhost:5001/api/identity/auth/callback/keycloak","http://localhost:5001/api/identity/auth/logout-callback/keycloak"]' \
  -s 'attributes={"post.logout.redirect.uris":"+"}' \
  -s 'webOrigins=["http://localhost:5001"]' \
  -s 'defaultClientScopes=["web-origins","acr","roles","profile","email"]' >/dev/null

ensure_user() {
  email=$1
  user_id=$(
	"$kcadm" get users -r "$realm" -q "username=$email" \
	  | sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
	  | head -n 1
  )
  if [ -z "$user_id" ]; then
	echo "Imported Keycloak user '$email' is missing." >&2
	return 1
  fi

  "$kcadm" update "users/$user_id" -r "$realm" \
	-s enabled=true -s emailVerified=true >/dev/null
  "$kcadm" set-password -r "$realm" \
	--username "$email" \
	--new-password "$IDENTITY_KEYCLOAK_TEST_PASSWORD" >/dev/null
}

ensure_user admin-soporte@restaurantes.test
ensure_user gerente@restaurantes.test
ensure_user contabilidad@restaurantes.test
ensure_user contabilidad2@restaurantes.test
ensure_user marketing@restaurantes.test
ensure_user marketing2@restaurantes.test
ensure_user kds-madrid-centro@restaurantes.test
ensure_user commander-madrid-centro@restaurantes.test
ensure_user tpv-madrid-centro@restaurantes.test
ensure_user kds-bcn-centro@restaurantes.test
ensure_user commander-bcn-centro@restaurantes.test
ensure_user tpv-bcn-centro@restaurantes.test

echo "Keycloak realm, OIDC client and development accounts are ready."

# Identity

Identity autentica al personal mediante Keycloak con OpenID Connect. La aplicación no crea contraseñas ni permite el registro libre. Un Admin autoriza previamente cada correo en Backoffice y le asigna un rol y un alcance: `Todos los Locales` o exactamente un local.

## Flujo

1. El usuario pulsa `Iniciar sesión`; el cliente abre GET /api/identity/auth/start?returnPath=... y Identity redirige directamente a Keycloak mediante OpenID Connect, código de autorización y PKCE.
2. Identity valida la respuesta OIDC, exige las claims `sub`, `email` y `email_verified=true`, busca el correo autorizado y vincula su identificador Keycloak en el primer acceso.
3. Identity devuelve un código de un solo uso en el fragmento de la URL del cliente. El cliente lo intercambia mediante POST /api/identity/auth/exchange por el JWT interno de Restaurantes.
5. Las API aplican los permisos definidos en Restaurantes.Security. AuthorizeView controla la presentación en Backoffice; la autorización de datos y operaciones se realiza en las API.

El código de intercambio caduca a los dos minutos y se consume de forma atómica. El JWT utiliza `Security:AccessTokenMinutes`, cuyo valor predeterminado central es 480 minutos. Los cambios de permisos de una sesión existente surten efecto al caducar el token.

## Cuentas y roles

La tabla `authorized_accounts` contiene ID, correo, proveedor fijo `Keycloak`, identificador Keycloak, nombre, rol, alcance, local opcional y estado. El correo permite el alta previa; tras el primer acceso se vincula el `sub` estable de Keycloak. Solo se aceptan emails verificados.

Los perfiles administrativos (`Admin`, `Gerente`, `Contabilidad` y `Marketing`) pueden consultar la información de varios locales y elegir el local en las vistas administrativas. Los perfiles operativos (`Manager`, `Camarero` y `Kds`) se asignan a un único local; TPV, Comandero y KDS usan ese local sin mostrar un selector de locales. No existe una selección de varios locales concretos para una cuenta operativa.

El alcance se modela explícitamente en PostgreSQL mediante `AllRestaurants` y `RestaurantId`. La restricción `CK_authorized_accounts_restaurant_scope` admite únicamente estas combinaciones:

- `AllRestaurants = true` y `RestaurantId = NULL`: el rol y sus permisos se aplican a todos los locales;
- `AllRestaurants = false` y `RestaurantId` informado: el rol y sus permisos se aplican solo a ese local.

La migración `AddExplicitRestaurantScope` conserva las cuentas existentes y explicita su alcance. La migración histórica `ResetAuthorizedAccounts` eliminó las cuentas al aplicarse. `UseKeycloakAuthentication` elimina `authentication_settings`, normaliza el proveedor de las cuentas existentes a `Keycloak` y borra sus identificadores externos anteriores para que se vinculen al iniciar sesión de nuevo. Si encuentra correos duplicados ignorando mayúsculas/minúsculas, se detiene y exige resolverlos antes de migrar.

En el JWT, un alcance `Todos los Locales` se representa con una claim de rol. Un alcance de un solo local se representa con claims `restaurant_id`, `restaurant_role` y `restaurant_permission` vinculadas a ese identificador. Las API evalúan los mismos permisos de rol en ambos alcances.

El Backoffice presenta `Todos los Locales` como primera opción del selector. Un Admin con alcance total puede asignar cualquier alcance; un Admin limitado a un local solo puede consultar y administrar cuentas de ese mismo local y no puede elevarlas a `Todos los Locales`.

El correo verificado de Keycloak se comprueba antes de autorizar una cuenta. No se puede volver a autorizar el mismo correo para otro local.

El Backoffice permite eliminar una cuenta mediante `DELETE /api/identity/users/{id}`. La API impide eliminar la cuenta autenticada, aplica el mismo alcance de administración utilizado para consultar y editar usuarios y rechaza la eliminación del último Admin activo.

Manager opera el TPV/POS y puede cobrar pedidos de cualquier origen. Camarero opera el Comandero, toma pedidos y los envía a cocina; también puede cobrar con tarjeta únicamente los pedidos creados con su cuenta y liberar la mesa después de cobrarla. Los cobros en efectivo o transferencia y los pedidos de otros camareros quedan para el TPV.

Ya no existe una tabla ni un endpoint para elegir proveedor: el único proveedor OIDC es Keycloak. Las migraciones anteriores se conservan como historial; una nueva migración elimina `authentication_settings` sin alterar las migraciones ya aplicadas.

## Configuración

El proyecto `Restaurantes.Identity.Api` tiene un `UserSecretsId`. En desarrollo, los secretos exclusivos de Identity se configuran mediante .NET User Secrets o Vault; en producción, mediante el almacén de secretos o las variables del entorno de despliegue. Compose inicia la imagen oficial de Keycloak en `http://localhost:8080`. Al arrancar, Keycloak importa `tools/keycloak/import/realm-restaurantes.json` porque Compose monta esa carpeta en `/opt/keycloak/data/import` y pasa `--import-realm`; ese JSON crea el realm y sus usuarios sin contraseñas versionadas. Después, `keycloak-init` registra el cliente OIDC confidencial y asigna la contraseña local desde el `.env`. `vault-init` guarda la configuración de Identity en Vault, desde donde la API la carga al arrancar.

Copia `tools/vault/.env.example` a `tools/vault/.env` y define las contraseñas locales de `KEYCLOAK_ADMIN_PASSWORD`, `IDENTITY_KEYCLOAK_CLIENT_SECRET` e `IDENTITY_KEYCLOAK_TEST_PASSWORD`, además de los demás valores requeridos por Compose. No compartas ni versionas ese archivo. Después ejecuta `docker compose --env-file tools/vault/.env up -d`; inicia `Restaurantes.Identity.Api` desde Visual Studio una vez finalice la inicialización. El cliente queda registrado con el callback `http://localhost:5001/api/identity/auth/callback/keycloak` y los destinos de retorno de logout. El botón Salir usa `/api/identity/auth/logout` para cerrar la sesión SSO de Keycloak además de la sesión local. El panel de Keycloak está en `http://localhost:8080/admin/`; las cuentas importadas usan la contraseña indicada por `IDENTITY_KEYCLOAK_TEST_PASSWORD` (solo local). Para que puedan entrar a la aplicación, ejecuta también el seed de desarrollo que autoriza esas cuentas en `authorized_accounts`. Keycloak importa el realm solo si aún no existe en el volumen persistente; los cambios posteriores al JSON no reemplazan automáticamente el realm ya creado.

- `ExternalAuth:PublicOrigin`: origen público del gateway.
- `ExternalAuth:Keycloak:Authority`: URL pública del realm, por ejemplo `https://<host>/realms/<realm>`.
- `ExternalAuth:Keycloak:ClientId`: cliente OIDC confidencial configurado en Keycloak.
- `ExternalAuth:Keycloak:ClientSecret`: secreto del cliente, solo en User Secrets, Vault o variables de entorno.
- `ConnectionStrings:IdentityWrite`: conexión a PostgreSQL.

`IdentityBootstrap` se eliminó: la aplicación ya no crea ni actualiza usuarios al arrancar. El seed `tools/postgres/seed/01-development-catalog.sql` crea las cuentas de desarrollo de Keycloak. En un entorno nuevo de producción, un operador debe autorizar una primera cuenta Admin una sola vez, después de migrar, con SQL controlado en el esquema `identity`; por ejemplo:

```sql
SET search_path TO identity;

INSERT INTO authorized_accounts (
    "Id", "Email", "Provider", "ProviderSubject", "TenantId", "DisplayName", "Role",
    "AllRestaurants", "RestaurantId", "IsActive", "CreatedAtUtc"
)
VALUES (
    gen_random_uuid(), lower('admin-soporte@restaurantes.test'), 'Keycloak', NULL, NULL,
    'Administrador', 'Admin', true, NULL, true, now()
)
ON CONFLICT ("Provider", "Email") DO NOTHING;
```

`Security:SigningKey` no es un secreto exclusivo de Identity: todas las API que validan el JWT necesitan el mismo valor. Su configuración local y productiva se describe en [Configuración JWT compartida](../README.md#configuración-jwt-compartida).

ASP.NET Core administra automáticamente las claves de Data Protection utilizadas por el estado temporal de OIDC. En el entorno local de una sola instancia no se configura una ruta manual. Un despliegue con varias réplicas o almacenamiento efímero debe proporcionar un key ring persistente y compartido como parte de su infraestructura.

En .NET, una variable de entorno representa los dos puntos con doble guion bajo, por ejemplo `ExternalAuth__Keycloak__Authority`. `appsettings.json` contiene los campos públicos `Authority` y `ClientId`, vacíos por defecto para no inventar un realm ni un cliente; `ClientSecret` no se agrega allí. No se deben colocar secretos como `ClientSecret`, contraseñas reales o claves de firma en `appsettings.json`, documentación, código, imágenes ni archivos versionados.

El inicializador local registra el cliente como confidencial, con Authorization Code, PKCE y los scopes `profile` y `email`; habilita también las URI permitidas para el retorno de logout. El botón Salir cierra las sesiones de la aplicación y Keycloak, y vuelve al cliente indicado. El ID token debe incluir `email`, `email_verified` y `sub`. La URI de retorno local es `http://localhost:5001/api/identity/auth/callback/keycloak`. Los usuarios, opciones del realm y otros elementos compatibles se pueden declarar en el JSON de importación; los secretos se inyectan durante la inicialización desde `.env`, no se guardan en JSON versionado. El realm debe ser accesible desde el navegador y desde Identity para descubrimiento OIDC y canje del código. En otros ambientes, configura el cliente y el origen público para que coincidan con el despliegue.

## Rutas y proyectos

El inicio de sesión acepta solo estos destinos internos: /backoffice/, /pos/, /commander/, /kds/ y /dashboard/. El gateway YARP publica /api/identity/{**catch-all} hacia Identity. La aplicación puede alojarse en cualquier proveedor de nube.

- Restaurantes.Identity.Domain: cuentas y tickets de login.
- Restaurantes.Identity.Application: reglas de autorización y contratos.
- Restaurantes.Identity.Infrastructure: EF Core, PostgreSQL, códigos de intercambio y JWT.
- Restaurantes.Identity.Api: endpoints y OIDC.
- Restaurantes.Security: permisos, claims y validación del JWT en las API.

La API aplica únicamente las migraciones pendientes al arrancar; no inserta ni reescribe cuentas de usuario en cada inicio. `ResetAuthorizedAccounts` es una migración histórica destructiva. `UseKeycloakAuthentication` conserva las autorizaciones y su alcance, pero borra los vínculos externos antiguos, que se restablecen en el siguiente acceso verificado.

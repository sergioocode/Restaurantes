# Identity

Identity autentica al personal con cuentas corporativas de Microsoft o Google Workspace. La aplicación no crea contraseñas ni permite el registro libre. Un Admin autoriza previamente cada correo en Backoffice y le asigna un rol y un alcance: `Todos los Locales` o exactamente un local.

## Flujo

1. El cliente consulta GET /api/identity/auth/provider para mostrar el proveedor activo. El botón del proveedor no configurado o inactivo queda deshabilitado.
2. El navegador abre GET /api/identity/auth/start/{provider}?returnPath=... Identity utiliza OpenID Connect con código de autorización y PKCE.
3. Identity valida el token del proveedor, el tenant de Microsoft o el dominio Workspace de Google, el correo autorizado, el estado de la cuenta y el identificador estable del proveedor.
4. Identity devuelve un código de un solo uso en el fragmento de la URL del cliente. El cliente lo intercambia mediante POST /api/identity/auth/exchange por el JWT interno de Restaurantes.
5. Las API aplican los permisos definidos en Restaurantes.Security. AuthorizeView controla la presentación en Backoffice; la autorización de datos y operaciones se realiza en las API.

El código de intercambio caduca a los dos minutos y se consume de forma atómica. El JWT de desarrollo caduca a los quince minutos. Los cambios de permisos de una sesión existente surten efecto al caducar el token.

## Cuentas y roles

La tabla `authorized_accounts` contiene ID, correo, proveedor, identificador del proveedor, tenant, nombre, rol, alcance, local opcional y estado. El correo permite el alta previa; tras el primer acceso se vincula el identificador estable de la cuenta externa.

En Microsoft Entra ID, el `TenantId` es la restricción principal: solo se aceptan miembros del tenant configurado (`acct = 0`) y se rechazan invitados. El sufijo del correo no está fijado en el código; al crear el usuario en Entra ID debe usarse uno de los dominios verificados del tenant y el correo debe coincidir exactamente con la cuenta autorizada en Backoffice. Por tanto, `kds-local01@restaurante.es` solo será válido si `restaurante.es` está verificado en ese tenant. Google sí restringe explícitamente el dominio mediante `ExternalAuth:Google:WorkspaceDomain`.

El rol y el alcance son conceptos independientes. Cualquiera de los roles `Admin`, `Gerente`, `Contabilidad`, `Marketing`, `Manager`, `Camarero` y `Kds` puede asignarse a `Todos los Locales` o a un único local. No existe una selección de varios locales concretos.

El alcance se modela explícitamente en PostgreSQL mediante `AllRestaurants` y `RestaurantId`. La restricción `CK_authorized_accounts_restaurant_scope` admite únicamente estas combinaciones:

- `AllRestaurants = true` y `RestaurantId = NULL`: el rol y sus permisos se aplican a todos los locales;
- `AllRestaurants = false` y `RestaurantId` informado: el rol y sus permisos se aplican solo a ese local.

La migración `AddExplicitRestaurantScope` conserva las cuentas existentes y explicita su alcance. La migración posterior `ResetAuthorizedAccounts` elimina todas las cuentas autorizadas y sus tickets de acceso asociados para reiniciar la autorización. En el siguiente arranque, al quedar la tabla vacía, Identity crea únicamente el Admin definido por `IdentityBootstrap:AdminEmail`, con el proveedor de `ExternalAuth:Provider` y alcance `Todos los Locales`.

En el JWT, un alcance `Todos los Locales` se representa con una claim de rol. Un alcance de un solo local se representa con claims `restaurant_id`, `restaurant_role` y `restaurant_permission` vinculadas a ese identificador. Las API evalúan los mismos permisos de rol en ambos alcances.

El Backoffice presenta `Todos los Locales` como primera opción del selector. Un Admin con alcance total puede asignar cualquier alcance; un Admin limitado a un local solo puede consultar y administrar cuentas de ese mismo local y no puede elevarlas a `Todos los Locales`.

Manager opera el TPV/POS, registra los pagos y libera las mesas al completar el cobro. Camarero opera el Comandero, toma pedidos y los envía a cocina, pero no registra pagos ni libera mesas.

La tabla authentication_settings conserva el proveedor activo para compatibilidad y auditoría. El proveedor efectivo se define en `ExternalAuth:Provider`; las cuentas del proveedor distinto no pueden iniciar sesión.

La migración histórica `ReplacePasswordIdentityWithExternalAccounts` descartó el modelo de usuarios anterior y creó las tablas actuales de cuentas autorizadas, configuración y códigos de intercambio. No debe confundirse con `AddExplicitRestaurantScope`, que conserva las cuentas y explicita su alcance.

## Configuración fuera de Git

El proyecto Restaurantes.Identity.Api.Write tiene un UserSecretsId. En desarrollo se pueden configurar estas claves mediante .NET User Secrets; en producción, mediante un almacén de secretos o variables de entorno:

- IdentityBootstrap:AdminEmail: correo corporativo del primer Admin. Solo se usa cuando la tabla de cuentas está vacía; la cuenta bootstrap se crea con alcance `Todos los Locales`.
- ExternalAuth:Provider: Microsoft o Google. Es el proveedor efectivo del despliegue y debe coincidir con las credenciales configuradas.
- ExternalAuth:PublicOrigin: origen público del gateway.
- ExternalAuth:DataProtectionKeysPath: directorio privado y persistente para las claves que protegen la sesión temporal OIDC. En despliegues con varias réplicas debe ser compartido por ellas.
- ExternalAuth:Microsoft:TenantId: identificador del tenant de trabajo.
- ExternalAuth:Microsoft:ClientId: identificador de la aplicación registrada.
- ExternalAuth:Microsoft:ClientSecret: secreto de la aplicación guardado en el servidor.
- ExternalAuth:Google:WorkspaceDomain: dominio corporativo de Workspace.
- ExternalAuth:Google:ClientId y ExternalAuth:Google:ClientSecret: credenciales del cliente OIDC.
- Security:SigningKey: clave de firma compartida con las API que validan el JWT.
- ConnectionStrings:IdentityWrite: conexión a PostgreSQL.

En .NET, una variable de entorno representa los dos puntos con doble guion bajo, por ejemplo ExternalAuth__Microsoft__TenantId. No se deben colocar valores reales en appsettings.json, documentación, código, imágenes ni archivos versionados.

La aplicación de Microsoft se registra para cuentas de esta organización únicamente, con plataforma Web. El sufijo disponible al crear usuarios depende de los dominios verificados del tenant. Hay que incluir la claim opcional acct en el ID token; Identity exige acct = 0 (miembro del tenant) y rechaza invitados. Su URI de retorno es:

    {ExternalAuth:PublicOrigin}/api/identity/auth/callback/microsoft

La aplicación de Google Workspace usa:

    {ExternalAuth:PublicOrigin}/api/identity/auth/callback/google

El origen público y las URI de retorno deben coincidir exactamente con la configuración de cada proveedor. Google queda implementado, pero su inicio de sesión real requiere Workspace y una cuenta corporativa para la prueba.

## Rutas y proyectos

El inicio de sesión acepta solo estos destinos internos: /backoffice/, /pos/, /commander/, /kds/ y /dashboard/. El gateway YARP publica /api/identity/{**catch-all} hacia Identity. La aplicación puede alojarse en cualquier proveedor de nube.

- Restaurantes.Identity.Domain: cuentas y configuración.
- Restaurantes.Identity.Application: reglas de autorización y contratos.
- Restaurantes.Identity.Infrastructure: EF Core, PostgreSQL, códigos de intercambio y JWT.
- Restaurantes.Identity.Api.Write: endpoints y OIDC.
- Restaurantes.Security: permisos, claims y validación del JWT en las API.

La API aplica las migraciones pendientes al arrancar. `ResetAuthorizedAccounts` elimina las cuentas autorizadas para reiniciar su configuración; esta operación no es reversible porque la migración no conserva sus datos.

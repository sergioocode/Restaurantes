# Identity

Identity autentica al personal con cuentas corporativas de Microsoft o Google Workspace. La aplicación no crea contraseñas ni permite el registro libre. Un Admin autoriza previamente cada correo en Backoffice y le asigna un rol y, para los roles locales, un único restaurante.

## Flujo

1. El cliente consulta GET /api/identity/auth/provider para mostrar el proveedor activo. El botón del proveedor no configurado o inactivo queda deshabilitado.
2. El navegador abre GET /api/identity/auth/start/{provider}?returnPath=... Identity utiliza OpenID Connect con código de autorización y PKCE.
3. Identity valida el token del proveedor, el tenant de Microsoft o el dominio Workspace de Google, el correo autorizado, el estado de la cuenta y el identificador estable del proveedor.
4. Identity devuelve un código de un solo uso en el fragmento de la URL del cliente. El cliente lo intercambia mediante POST /api/identity/auth/exchange por el JWT interno de Restaurantes.
5. Las API aplican los permisos definidos en Restaurantes.Security. AuthorizeView controla la presentación en Backoffice; la autorización de datos y operaciones se realiza en las API.

El código de intercambio caduca a los dos minutos y se consume de forma atómica. El JWT de desarrollo caduca a los quince minutos. Los cambios de permisos de una sesión existente surten efecto al caducar el token.

## Cuentas y roles

La tabla authorized_accounts contiene ID, correo, proveedor, identificador del proveedor, tenant, nombre, rol, local opcional y estado. El correo permite el alta previa; tras el primer acceso se vincula el identificador estable de la cuenta externa.

Admin, Gerente, Contabilidad y Oficina son globales y no tienen local. Manager, PosComandero y Kds requieren exactamente un local. Una cuenta local no puede operar en otro restaurante.

La tabla authentication_settings almacena el proveedor activo. Solo el Admin puede cambiarlo y debe existir un Admin activo del proveedor de destino. Las cuentas del proveedor inactivo no pueden iniciar sesión.

La migración ReplacePasswordIdentityWithExternalAccounts descarta todos los usuarios y asignaciones anteriores, elimina las tablas de ASP.NET Core Identity y crea las tablas de cuentas autorizadas, configuración y códigos de intercambio.

## Configuración fuera de Git

El proyecto Restaurantes.Identity.Api.Write tiene un UserSecretsId. En desarrollo se pueden configurar estas claves mediante .NET User Secrets; en producción, mediante un almacén de secretos o variables de entorno:

- IdentityBootstrap:AdminEmail: correo corporativo del primer Admin. Solo se usa cuando la tabla de cuentas está vacía.
- IdentityBootstrap:Provider: Microsoft o Google.
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

La aplicación de Microsoft se registra para cuentas de esta organización únicamente, con plataforma Web. Hay que incluir la claim opcional acct en el ID token; Identity exige acct = 0 (miembro del tenant) y rechaza invitados. Su URI de retorno es:

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

La API aplica migraciones al arrancar. Antes de usar una base existente, se debe considerar que esta migración elimina los usuarios anteriores.

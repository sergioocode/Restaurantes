-- Reconstruye el catálogo de desarrollo tras tools/postgres/reset-operational-data.ps1,
-- reemplaza las estaciones KDS de los cinco locales y agrega descuentos de ejemplo.
-- No limpia pedidos; para eso usa tools/postgres/reset-operational-data.sql.
--
-- Antes de empezar:
--   1. Asegúrate de que las migraciones de Identity, RestaurantOperations, Catalog, Orders y Dining
--      estén aplicadas en restaurants_operational.
--   2. Detén todos los servicios y workers de la aplicación.
--   3. Ejecuta .\tools\postgres\reset-operational-data.ps1 -ConfirmReset BORRAR
--      para vaciar los datos operativos y el catálogo en ambas bases y purgar sus colas.
--   4. Desde PowerShell en la raíz del repositorio, copia el archivo al contenedor:
--      docker cp .\tools\postgres\seed\01-development-catalog.sql restaurantes-postgres:/tmp/01-development-catalog.sql
--   5. Ejecuta el seed:
--      docker exec -i restaurantes-postgres psql -U restaurants -d restaurants_operational -v ON_ERROR_STOP=1 -f /tmp/01-development-catalog.sql
--   6. Arranca de nuevo los servicios y workers para que procesen los eventos del outbox
--      y actualicen las proyecciones de lectura.
--
-- El reset elimina también las asociaciones de fotos; los blobs externos no se borran aquí.
-- Ejecuta únicamente en el entorno local de desarrollo.

\set ON_ERROR_STOP on

\connect restaurants_operational
SET search_path TO restaurant_operations;

WITH inserted AS (
    INSERT INTO companies (
        "Id", "LegalName", "TaxId", "FiscalAddress", "Phone", "Email", "Version", "UpdatedAtUtc"
    )
    VALUES (
        '99999999-9999-4999-8999-999999999999',
        'Restaurantes Españita S.L.',
        'A12345678',
        'Cruz del Carnero 6',
        NULL,
        NULL,
        1,
        now()
    )
    ON CONFLICT ("TaxId") DO UPDATE SET
        "LegalName" = EXCLUDED."LegalName",
        "FiscalAddress" = EXCLUDED."FiscalAddress",
        "Version" = companies."Version" + 1,
        "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.RestaurantOperations.Contracts.Events.CompanyChanged',
       jsonb_build_object(
           'CompanyId', "Id", 'LegalName', "LegalName", 'TaxId', "TaxId",
           'FiscalAddress', "FiscalAddress", 'Phone', "Phone", 'Email', "Email",
           'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;

WITH company AS (
    SELECT "Id" AS "CompanyId"
    FROM companies
    WHERE "TaxId" = 'A12345678'
), seed("Id", "Code", "Name", "Address", "IsActive", "Version", "UpdatedAtUtc") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid, 'MAD-CENTRO', 'Local Madrid Centro', 'Calle Mayor 10, Madrid', true, 1, now()),
        ('22222222-2222-4222-8222-222222222222'::uuid, 'MAD-NORTE', 'Local Madrid Norte', 'Paseo de la Castellana 200, Madrid', true, 1, now()),
        ('33333333-3333-4333-8333-333333333333'::uuid, 'BCN-CENTRO', 'Local Barcelona Centro', 'Carrer de Mallorca 100, Barcelona', true, 1, now()),
        ('44444444-4444-4444-8444-444444444444'::uuid, 'VAL-CENTRO', 'Local Valencia Centro', 'Carrer de Colón 50, Valencia', true, 1, now()),
        ('55555555-5555-4555-8555-555555555555'::uuid, 'SEV-CENTRO', 'Local Sevilla Centro', 'Calle Sierpes 25, Sevilla', true, 1, now())
), inserted AS (
    INSERT INTO restaurants (
        "Id", "Code", "Name", "Address", "IsActive", "Version", "UpdatedAtUtc", "CompanyId"
    )
    SELECT seed."Id", seed."Code", seed."Name", seed."Address", seed."IsActive",
           seed."Version", seed."UpdatedAtUtc", company."CompanyId"
    FROM seed CROSS JOIN company
    WHERE true
    ON CONFLICT ("Code") DO UPDATE SET
        "Name" = EXCLUDED."Name",
        "Address" = EXCLUDED."Address",
        "IsActive" = EXCLUDED."IsActive",
        "CompanyId" = EXCLUDED."CompanyId",
        "Version" = restaurants."Version" + 1,
        "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.RestaurantOperations.Contracts.Events.RestaurantChanged',
       jsonb_build_object(
           'RestaurantId', "Id", 'Code', "Code", 'Name', "Name", 'Address', "Address", 'CompanyId', "CompanyId",
           'IsActive', "IsActive", 'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;

UPDATE outbox_messages
SET "ProcessedAtUtc" = NULL,
    "Error" = NULL,
    "Type" = 'Restaurantes.RestaurantOperations.Contracts.Events.RestaurantChanged'
WHERE "Type" = 'Restaurantes.RestaurantOperations.Contracts.RestaurantChanged';

\connect restaurants_operational
SET search_path TO identity;

WITH accounts("Email", "DisplayName", "Role", "RestaurantId") AS (
    VALUES
        ('kds-madrid-centro@facturacen.cl', 'KDS Madrid Centro', 'Kds', '11111111-1111-4111-8111-111111111111'::uuid),
        ('commander-madrid-centro@facturacen.cl', 'Commander Madrid Centro', 'Camarero', '11111111-1111-4111-8111-111111111111'::uuid),
        ('tpv-madrid-centro@facturacen.cl', 'TPV Madrid Centro', 'Manager', '11111111-1111-4111-8111-111111111111'::uuid),
        ('kds-bcn-centro@facturacen.cl', 'KDS Barcelona Centro', 'Kds', '33333333-3333-4333-8333-333333333333'::uuid),
        ('commander-bcn-centro@facturacen.cl', 'Commander Barcelona Centro', 'Camarero', '33333333-3333-4333-8333-333333333333'::uuid),
        ('tpv-bcn-centro@facturacen.cl', 'TPV Barcelona Centro', 'Manager', '33333333-3333-4333-8333-333333333333'::uuid)
)
INSERT INTO authorized_accounts (
    "Id", "Email", "Provider", "ProviderSubject", "TenantId", "DisplayName", "Role",
    "AllRestaurants", "RestaurantId", "IsActive", "CreatedAtUtc"
)
SELECT gen_random_uuid(), lower(account."Email"), settings."ActiveProvider", NULL, NULL,
       account."DisplayName", account."Role", false, account."RestaurantId", true, now()
FROM accounts account
CROSS JOIN authentication_settings settings
WHERE settings."Id" = 1
ON CONFLICT ("Provider", "Email") DO UPDATE SET
    "DisplayName" = EXCLUDED."DisplayName",
    "Role" = EXCLUDED."Role",
    "AllRestaurants" = EXCLUDED."AllRestaurants",
    "RestaurantId" = EXCLUDED."RestaurantId",
    "IsActive" = EXCLUDED."IsActive";

\connect restaurants_operational
SET search_path TO catalog_write;

INSERT INTO taxes ("Id", "Code", "Name", "Rate", "IsActive")
VALUES (
    'd02602b4-1eca-4350-9b65-1fade1330108'::uuid,
    'IVA-REDUCIDO-10-D02602B4',
    'IVA Reducido 10%',
    10.0000,
    true
)
ON CONFLICT ("Id") DO UPDATE SET
    "Code" = EXCLUDED."Code",
    "Name" = EXCLUDED."Name",
    "Rate" = EXCLUDED."Rate",
    "IsActive" = true;

INSERT INTO taxes ("Id", "Code", "Name", "Rate", "IsActive")
VALUES (
    'd02602b4-1eca-4350-9b65-1fade1330121'::uuid,
    'IVA-GENERAL-21-D02602B4',
    'IVA General 21%',
    21.0000,
    true
)
ON CONFLICT ("Id") DO UPDATE SET
    "Code" = EXCLUDED."Code",
    "Name" = EXCLUDED."Name",
    "Rate" = EXCLUDED."Rate",
    "IsActive" = true;

INSERT INTO allergens ("Id", "Code", "Name", "Description", "IsActive") VALUES
    ('00000000-0000-0000-0000-00000000a001', 'GLU', 'Gluten', 'Cereales con gluten (trigo, centeno, cebada, avena, espelta) y sus derivados', true),
    ('00000000-0000-0000-0000-00000000a002', 'CRU', 'Crustáceos', 'Gambas, langostinos, cangrejos, cigalas, nécoras y derivados', true),
    ('00000000-0000-0000-0000-00000000a003', 'HUE', 'Huevos', 'Huevos y productos a base de huevo (mayonesas, masas, empanados)', true),
    ('00000000-0000-0000-0000-00000000a004', 'PES', 'Pescado', 'Pescado y productos a base de pescado (caldos, salsas, gelatinas)', true),
    ('00000000-0000-0000-0000-00000000a005', 'CAC', 'Cacahuetes', 'Cacahuetes, mantecas, aceites y productos derivados', true),
    ('00000000-0000-0000-0000-00000000a006', 'SOJ', 'Soja', 'Soja y productos a base de soja (tofu, salsa de soja, lecitina)', true),
    ('00000000-0000-0000-0000-00000000a007', 'LAC', 'Lácteos', 'Leche y sus derivados, incluida la lactosa (queso, nata, mantequilla, yogur)', true),
    ('00000000-0000-0000-0000-00000000a008', 'FRU', 'Frutos secos', 'Frutos de cáscara (almendras, avellanas, nueces, anacardos, pistachos)', true),
    ('00000000-0000-0000-0000-00000000a009', 'API', 'Apio', 'Apio y productos derivados (hojas, semillas, en polvo)', true),
    ('00000000-0000-0000-0000-00000000a00a', 'MOS', 'Mostaza', 'Mostaza y productos derivados (semillas, salsas, aderezos)', true),
    ('00000000-0000-0000-0000-00000000a00b', 'SES', 'Sésamo', 'Granos de sésamo y productos a base de sésamo (tahini, aceites)', true),
    ('00000000-0000-0000-0000-00000000a00c', 'SUL', 'Sulfitos', 'Dióxido de azufre y sulfitos en concentraciones superiores a 10 mg/kg o 10 mg/l (vinos, vinagres)', true),
    ('00000000-0000-0000-0000-00000000a00d', 'ALT', 'Altramuces', 'Altramuces y productos a base de altramuces', true),
    ('00000000-0000-0000-0000-00000000a00e', 'MOL', 'Moluscos', 'Moluscos y productos a base de moluscos (mejillones, calamares, pulpo)', true)
ON CONFLICT ("Id") DO UPDATE SET
    "Code" = EXCLUDED."Code",
    "Name" = EXCLUDED."Name",
    "Description" = EXCLUDED."Description",
    "IsActive" = EXCLUDED."IsActive";

-- Los identificadores del seed son estables; un reset previo elimina los registros de fotos.
WITH inserted AS (
    INSERT INTO categories (
        "Id", "Code", "Name", "DefaultStationCode", "DefaultStationName",
        "IsActive", "Version", "UpdatedAtUtc", "ShowInPos", "ShowInCommander", "ShowInCustomerQr", "RequiresKds"
    )
    VALUES
        ('a1000000-0000-4000-8000-000000000001', 'PASTAS', 'Pastas', 'PASTAS', 'Pastas', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000002', 'BEBIDAS', 'Bebidas', 'BEBIDAS', 'Bebidas', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000003', 'CARNES', 'Carnes', 'CARNES', 'Carnes', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000004', 'POSTRES', 'Postres', 'POSTRES', 'Postres', true, 1, now(), true, true, true, true),
        ('10000000-0000-4000-8000-000000000001', 'ENTRANTES', 'Entrantes', 'ENTRANTES', 'Entrantes', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000005', 'ENSALADAS', 'Ensaladas', 'ENTRANTES', 'Entrantes', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000006', 'EXTRAS', 'Extras', 'ENTRANTES', 'Entrantes', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000007', 'CERVEZAS', 'Cervezas', 'BEBIDAS', 'Bebidas', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000008', 'VINOS', 'Vinos', 'BEBIDAS', 'Bebidas', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000009', 'AGUAS', 'Aguas', 'BEBIDAS', 'Bebidas', true, 1, now(), true, true, true, true),
        ('a1000000-0000-4000-8000-000000000010', 'MERCH', 'Merch', '', '', true, 1, now(), true, false, true, false),
        ('a1000000-0000-4000-8000-000000000011', 'PROMO', 'Promo', 'ENTRANTES', 'Entrantes', true, 1, now(), true, true, true, true)
    ON CONFLICT ("Id") DO UPDATE SET
        "Code" = EXCLUDED."Code",
        "Name" = EXCLUDED."Name",
        "DefaultStationCode" = EXCLUDED."DefaultStationCode",
        "DefaultStationName" = EXCLUDED."DefaultStationName",
        "IsActive" = EXCLUDED."IsActive",
        "ShowInPos" = EXCLUDED."ShowInPos",
        "ShowInCommander" = EXCLUDED."ShowInCommander",
        "ShowInCustomerQr" = EXCLUDED."ShowInCustomerQr",
        "RequiresKds" = EXCLUDED."RequiresKds",
        "Version" = categories."Version" + 1,
        "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.Events.CategoryChanged',
       jsonb_build_object(
           'CategoryId', "Id", 'Code', "Code", 'Name', "Name",
           'DefaultStationCode', "DefaultStationCode", 'DefaultStationName', "DefaultStationName",
           'IsActive', "IsActive", 'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
           'ShowInPos', "ShowInPos", 'ShowInCommander', "ShowInCommander", 'ShowInCustomerQr', "ShowInCustomerQr",
           'RequiresKds', "RequiresKds"
       ),
       "UpdatedAtUtc"
FROM inserted;

WITH inserted AS (
    INSERT INTO products ("Id", "Sku", "Name", "CategoryId", "IsActive", "Version", "UpdatedAtUtc")
    VALUES
        ('b1000000-0000-4000-8000-000000000001', 'P001', 'Spaghetti carbonara', 'a1000000-0000-4000-8000-000000000001', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000002', 'P002', 'Tagliatelle boloñesa', 'a1000000-0000-4000-8000-000000000001', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000003', 'P003', 'Lasaña de la casa', 'a1000000-0000-4000-8000-000000000001', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000004', 'B001', 'Agua mineral 50 cl', 'a1000000-0000-4000-8000-000000000009', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000005', 'B002', 'Refresco de cola', 'a1000000-0000-4000-8000-000000000002', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000006', 'B003', 'Cerveza de la casa', 'a1000000-0000-4000-8000-000000000007', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000007', 'C001', 'Entrecot 250 g', 'a1000000-0000-4000-8000-000000000003', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000008', 'C002', 'Hamburguesa clásica', 'a1000000-0000-4000-8000-000000000003', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000009', 'C003', 'Pollo a la parrilla', 'a1000000-0000-4000-8000-000000000003', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000010', 'D001', 'Tiramisú', 'a1000000-0000-4000-8000-000000000004', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000011', 'D002', 'Tarta de queso', 'a1000000-0000-4000-8000-000000000004', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000012', 'D003', 'Helado de vainilla', 'a1000000-0000-4000-8000-000000000004', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000013', 'E001', 'Ensalada mixta', 'a1000000-0000-4000-8000-000000000005', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000014', 'A001', 'Croquetas caseras', '10000000-0000-4000-8000-000000000001', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000015', 'A002', 'Patatas bravas', '10000000-0000-4000-8000-000000000001', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000016', 'X001', 'Aceitunas', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000017', 'X002', 'Alcaparras', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000018', 'X003', 'Cebolla confit', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000019', 'X004', 'Champiñones', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000020', 'X005', 'Guanciale', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000021', 'X006', 'Jamón', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000022', 'X007', 'Lardo', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000023', 'X008', 'Mortadella', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000024', 'X009', 'Nduja', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000025', 'X010', 'Pistachos tostados', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000026', 'X011', 'Pesto', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000027', 'X012', 'Scamorza', 'a1000000-0000-4000-8000-000000000006', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000028', 'B004', 'Café solo', 'a1000000-0000-4000-8000-000000000002', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000029', 'B005', 'Café con leche', 'a1000000-0000-4000-8000-000000000002', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000030', 'B006', 'Té e infusiones', 'a1000000-0000-4000-8000-000000000002', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000031', 'B007', 'Zumo de naranja natural', 'a1000000-0000-4000-8000-000000000002', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000032', 'B008', 'Agua con gas 50 cl', 'a1000000-0000-4000-8000-000000000009', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000033', 'B009', 'Cerveza sin alcohol', 'a1000000-0000-4000-8000-000000000007', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000034', 'B010', 'Cerveza tostada', 'a1000000-0000-4000-8000-000000000007', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000035', 'B011', 'Vino tinto por copa', 'a1000000-0000-4000-8000-000000000008', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000036', 'B012', 'Vino blanco por copa', 'a1000000-0000-4000-8000-000000000008', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000037', 'B013', 'Vino rosado por copa', 'a1000000-0000-4000-8000-000000000008', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000038', 'AG001', 'Agua mineral 1 l', 'a1000000-0000-4000-8000-000000000009', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000039', 'AG002', 'Agua con gas 1 l', 'a1000000-0000-4000-8000-000000000009', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000040', 'AG003', 'Agua con limón 50 cl', 'a1000000-0000-4000-8000-000000000009', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000041', 'MER001', 'Camiseta del restaurante', 'a1000000-0000-4000-8000-000000000010', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000042', 'MER002', 'Taza del restaurante', 'a1000000-0000-4000-8000-000000000010', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000043', 'MER003', 'Bolsa de tela', 'a1000000-0000-4000-8000-000000000010', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000044', 'PR001', 'Croquetas de la semana', 'a1000000-0000-4000-8000-000000000011', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000045', 'PR002', 'Pasta del día', 'a1000000-0000-4000-8000-000000000011', true, 1, now()),
        ('b1000000-0000-4000-8000-000000000046', 'PR003', 'Hamburguesa del día', 'a1000000-0000-4000-8000-000000000011', true, 1, now())
    ON CONFLICT ("Id") DO UPDATE SET
        "Sku" = EXCLUDED."Sku",
        "Name" = EXCLUDED."Name",
        "CategoryId" = EXCLUDED."CategoryId",
        "TaxId" = CASE WHEN EXCLUDED."CategoryId" = 'a1000000-0000-4000-8000-000000000010'::uuid
            THEN 'd02602b4-1eca-4350-9b65-1fade1330121'::uuid
            ELSE 'd02602b4-1eca-4350-9b65-1fade1330108'::uuid END,
        "IsActive" = EXCLUDED."IsActive",
        "Version" = products."Version" + 1,
        "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.Events.ProductChanged',
       jsonb_build_object(
           'ProductId', "Id", 'Sku', "Sku", 'Name', "Name", 'CategoryId', "CategoryId",
           'IsActive', "IsActive", 'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;

UPDATE products
SET "TaxId" = CASE WHEN "CategoryId" = 'a1000000-0000-4000-8000-000000000010'::uuid
    THEN 'd02602b4-1eca-4350-9b65-1fade1330121'::uuid
    ELSE 'd02602b4-1eca-4350-9b65-1fade1330108'::uuid END
WHERE "Id" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                 AND 'b1000000-0000-4000-8000-000000000046'::uuid;

DO $$
BEGIN
    IF (
        SELECT count(*)
        FROM products
        WHERE "Id" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                        AND 'b1000000-0000-4000-8000-000000000046'::uuid
          AND "TaxId" = CASE WHEN "CategoryId" = 'a1000000-0000-4000-8000-000000000010'::uuid
              THEN 'd02602b4-1eca-4350-9b65-1fade1330121'::uuid
              ELSE 'd02602b4-1eca-4350-9b65-1fade1330108'::uuid END
    ) <> 46 THEN
        RAISE EXCEPTION 'No se pudo asociar el impuesto requerido a todos los productos del seed.';
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM products product
        LEFT JOIN taxes tax ON tax."Id" = product."TaxId"
        WHERE product."CategoryId" = 'a1000000-0000-4000-8000-000000000010'::uuid
          AND (tax."Id" IS NULL OR tax."Rate" <> 21.0000 OR tax."IsActive" = false)
    ) THEN
        RAISE EXCEPTION 'Los productos de Merch deben tener asociado el IVA general activo del 21%%.';
    END IF;
END $$;

DELETE FROM product_prices p
WHERE p."ProductId" IN (
    SELECT "Id" FROM products WHERE "Id" BETWEEN
        'b1000000-0000-4000-8000-000000000001'::uuid AND 'b1000000-0000-4000-8000-000000000046'::uuid
);

WITH seed_prices("Sku", "BasePrice", "Format", "FormatMultiplier") AS (
    VALUES
        ('P001', 12.50::numeric, 'PLATO', 1.00::numeric),
        ('P001', 12.50::numeric, 'RACION', 1.10::numeric),
        ('P001', 12.50::numeric, 'MEDIA_RACION', 0.80::numeric),
        ('P002', 13.00::numeric, 'PLATO', 1.00::numeric),
        ('P002', 13.00::numeric, 'MEDIA_RACION', 0.80::numeric),
        ('P003', 14.00::numeric, 'PLATO', 1.00::numeric),
        ('P003', 14.00::numeric, 'RACION', 1.10::numeric),
        ('B001', 2.20::numeric, 'UNIDAD', 1.00::numeric),
        ('B002', 3.25::numeric, 'UNIDAD', 1.00::numeric),
        ('B003', 3.50::numeric, 'UNIDAD', 1.00::numeric),
        ('B004', 1.50::numeric, 'UNIDAD', 1.00::numeric),
        ('B005', 1.80::numeric, 'UNIDAD', 1.00::numeric),
        ('B006', 1.60::numeric, 'UNIDAD', 1.00::numeric),
        ('B007', 3.50::numeric, 'UNIDAD', 1.00::numeric),
        ('B008', 2.50::numeric, 'UNIDAD', 1.00::numeric),
        ('B009', 3.00::numeric, 'UNIDAD', 1.00::numeric),
        ('B010', 3.50::numeric, 'UNIDAD', 1.00::numeric),
        ('B011', 4.00::numeric, 'COPA', 1.00::numeric),
        ('B012', 4.00::numeric, 'COPA', 1.00::numeric),
        ('B013', 4.00::numeric, 'COPA', 1.00::numeric),
        ('C001', 21.50::numeric, 'PLATO', 1.00::numeric),
        ('C001', 21.50::numeric, 'RACION', 1.10::numeric),
        ('C002', 13.50::numeric, 'PLATO', 1.00::numeric),
        ('C003', 15.00::numeric, 'PLATO', 1.00::numeric),
        ('D001', 6.50::numeric, 'PLATO', 1.00::numeric),
        ('D002', 6.80::numeric, 'PLATO', 1.00::numeric),
        ('D003', 5.50::numeric, 'PLATO', 1.00::numeric),
        ('E001', 9.50::numeric, 'PLATO', 1.00::numeric),
        ('E001', 9.50::numeric, 'MEDIA_RACION', 0.80::numeric),
        ('E001', 9.50::numeric, 'TAPA', 0.65::numeric),
        ('A001', 10.00::numeric, 'RACION', 1.00::numeric),
        ('A001', 10.00::numeric, 'MEDIA_RACION', 0.80::numeric),
        ('A001', 10.00::numeric, 'TAPA', 0.65::numeric),
        ('A002', 7.50::numeric, 'RACION', 1.00::numeric),
        ('A002', 7.50::numeric, 'TAPA', 0.65::numeric),
        ('X001', 1.00::numeric, 'RACION', 1.00::numeric),
        ('X002', 1.50::numeric, 'RACION', 1.00::numeric),
        ('X003', 2.50::numeric, 'RACION', 1.00::numeric),
        ('X004', 1.00::numeric, 'RACION', 1.00::numeric),
        ('X005', 2.50::numeric, 'RACION', 1.00::numeric),
        ('X006', 3.00::numeric, 'RACION', 1.00::numeric),
        ('X007', 1.50::numeric, 'RACION', 1.00::numeric),
        ('X008', 3.00::numeric, 'RACION', 1.00::numeric),
        ('X009', 1.00::numeric, 'RACION', 1.00::numeric),
        ('X010', 1.00::numeric, 'RACION', 1.00::numeric),
        ('X011', 2.50::numeric, 'RACION', 1.00::numeric),
        ('X012', 1.50::numeric, 'RACION', 1.00::numeric),
        ('AG001', 3.20::numeric, 'UNIDAD', 1.00::numeric),
        ('AG002', 3.50::numeric, 'UNIDAD', 1.00::numeric),
        ('AG003', 2.80::numeric, 'UNIDAD', 1.00::numeric),
        ('MER001', 18.00::numeric, 'UNIDAD', 1.00::numeric),
        ('MER002', 9.50::numeric, 'UNIDAD', 1.00::numeric),
        ('MER003', 6.00::numeric, 'UNIDAD', 1.00::numeric),
        ('PR001', 8.50::numeric, 'PLATO', 1.00::numeric),
        ('PR001', 8.50::numeric, 'TAPA', 0.65::numeric),
        ('PR002', 10.50::numeric, 'PLATO', 1.00::numeric),
        ('PR002', 10.50::numeric, 'MEDIA_RACION', 0.80::numeric),
        ('PR003', 11.00::numeric, 'PLATO', 1.00::numeric)
), channels("Channel", "Multiplier") AS (
    VALUES
        ('LOCAL', 1.00::numeric),
        ('DELIVERY', 1.10::numeric),
        ('GLOVO', 1.20::numeric),
        ('UBER', 1.25::numeric)
)
INSERT INTO product_prices ("Id", "ProductId", "RestaurantId", "Channel", "Format", "Amount")
SELECT gen_random_uuid(), product."Id", '00000000-0000-0000-0000-000000000000'::uuid,
       channel."Channel", seed."Format",
       round(seed."BasePrice" * seed."FormatMultiplier" * channel."Multiplier", 2)
FROM products product
JOIN seed_prices seed ON seed."Sku" = product."Sku"
CROSS JOIN channels channel
ON CONFLICT ("ProductId", "RestaurantId", "Channel", "Format") DO UPDATE
SET "Amount" = EXCLUDED."Amount";

DELETE FROM product_options
WHERE "GroupId" IN (
    SELECT "Id" FROM product_option_groups
    WHERE "ParentProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                                 AND 'b1000000-0000-4000-8000-000000000003'::uuid
);

DELETE FROM product_option_groups
WHERE "ParentProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                             AND 'b1000000-0000-4000-8000-000000000003'::uuid;

INSERT INTO product_option_groups (
    "Id", "ParentProductId", "Name", "Kind", "MinSelections", "MaxSelections", "SortOrder"
)
VALUES
    ('c1000000-0000-4000-8000-000000000001', 'b1000000-0000-4000-8000-000000000001', 'Extras', 'Extra', 0, 6, 0),
    ('c1000000-0000-4000-8000-000000000002', 'b1000000-0000-4000-8000-000000000002', 'Extras', 'Extra', 0, 6, 0),
    ('c1000000-0000-4000-8000-000000000003', 'b1000000-0000-4000-8000-000000000003', 'Extras', 'Extra', 0, 6, 0)
ON CONFLICT ("Id") DO UPDATE SET
    "ParentProductId" = EXCLUDED."ParentProductId",
    "Name" = EXCLUDED."Name",
    "Kind" = EXCLUDED."Kind",
    "MinSelections" = EXCLUDED."MinSelections",
    "MaxSelections" = EXCLUDED."MaxSelections",
    "SortOrder" = EXCLUDED."SortOrder";

WITH extra_choices("GroupId", "ProductSku", "ExtraSku", "SortOrder") AS (
    VALUES
        ('c1000000-0000-4000-8000-000000000001'::uuid, 'P001', 'X001', 0),
        ('c1000000-0000-4000-8000-000000000001'::uuid, 'P001', 'X003', 1),
        ('c1000000-0000-4000-8000-000000000001'::uuid, 'P001', 'X004', 2),
        ('c1000000-0000-4000-8000-000000000001'::uuid, 'P001', 'X005', 3),
        ('c1000000-0000-4000-8000-000000000001'::uuid, 'P001', 'X006', 4),
        ('c1000000-0000-4000-8000-000000000001'::uuid, 'P001', 'X012', 5),
        ('c1000000-0000-4000-8000-000000000002'::uuid, 'P002', 'X001', 0),
        ('c1000000-0000-4000-8000-000000000002'::uuid, 'P002', 'X004', 1),
        ('c1000000-0000-4000-8000-000000000002'::uuid, 'P002', 'X005', 2),
        ('c1000000-0000-4000-8000-000000000002'::uuid, 'P002', 'X006', 3),
        ('c1000000-0000-4000-8000-000000000002'::uuid, 'P002', 'X008', 4),
        ('c1000000-0000-4000-8000-000000000002'::uuid, 'P002', 'X012', 5),
        ('c1000000-0000-4000-8000-000000000003'::uuid, 'P003', 'X001', 0),
        ('c1000000-0000-4000-8000-000000000003'::uuid, 'P003', 'X003', 1),
        ('c1000000-0000-4000-8000-000000000003'::uuid, 'P003', 'X004', 2),
        ('c1000000-0000-4000-8000-000000000003'::uuid, 'P003', 'X006', 3),
        ('c1000000-0000-4000-8000-000000000003'::uuid, 'P003', 'X011', 4),
        ('c1000000-0000-4000-8000-000000000003'::uuid, 'P003', 'X012', 5)
)
INSERT INTO product_options ("GroupId", "ProductId", "SortOrder")
SELECT choices."GroupId", extra."Id", choices."SortOrder"
FROM extra_choices choices
JOIN products parent ON parent."Sku" = choices."ProductSku"
JOIN products extra ON extra."Sku" = choices."ExtraSku"
ON CONFLICT ("GroupId", "ProductId") DO UPDATE
SET "SortOrder" = EXCLUDED."SortOrder";

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM product_option_groups group_item
        LEFT JOIN product_options option_item ON option_item."GroupId" = group_item."Id"
        WHERE group_item."ParentProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                                                  AND 'b1000000-0000-4000-8000-000000000046'::uuid
          AND group_item."Kind" = 'Extra'
        GROUP BY group_item."ParentProductId", group_item."Id", group_item."MaxSelections"
        HAVING count(option_item."ProductId") > 6 OR group_item."MaxSelections" > 6
    ) THEN
        RAISE EXCEPTION 'Un producto del seed tiene más de seis extras configurados.';
    END IF;
END $$;

UPDATE catalog_menus
SET "IsActive" = false
WHERE "RestaurantId" = '00000000-0000-0000-0000-000000000000'::uuid
  AND "IsActive" = true
  AND "Id" <> '11111111-1111-4111-8111-111111111111'::uuid;

INSERT INTO catalog_menus ("Id", "RestaurantId", "Name", "IsActive")
VALUES (
    '11111111-1111-4111-8111-111111111111',
    '00000000-0000-0000-0000-000000000000',
    'Carta general',
    true
)
ON CONFLICT ("Id") DO UPDATE
SET "Name" = EXCLUDED."Name", "IsActive" = EXCLUDED."IsActive";

DELETE FROM catalog_menu_prices
WHERE "MenuId" = '11111111-1111-4111-8111-111111111111'::uuid
  AND "ProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                       AND 'b1000000-0000-4000-8000-000000000046'::uuid;

INSERT INTO catalog_menu_prices ("MenuId", "ProductId", "Channel", "Format", "Amount")
SELECT '11111111-1111-4111-8111-111111111111'::uuid, p."ProductId", p."Channel", p."Format", p."Amount"
FROM product_prices p
WHERE p."RestaurantId" = '00000000-0000-0000-0000-000000000000'::uuid
  AND p."ProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                         AND 'b1000000-0000-4000-8000-000000000046'::uuid
ON CONFLICT ("MenuId", "ProductId", "Channel", "Format") DO UPDATE
SET "Amount" = EXCLUDED."Amount";

INSERT INTO catalog_menu_entries ("Id", "MenuId", "ProductId", "IsAvailable", "DefaultFormat", "SortOrder")
SELECT gen_random_uuid(), '11111111-1111-4111-8111-111111111111'::uuid, p."Id", true,
       default_price."Format",
       (row_number() OVER (ORDER BY p."Sku") - 1)::integer
FROM products p
CROSS JOIN LATERAL (
    SELECT price."Format"
    FROM product_prices price
    WHERE price."ProductId" = p."Id"
      AND price."RestaurantId" = '00000000-0000-0000-0000-000000000000'::uuid
      AND price."Channel" = 'LOCAL'
    ORDER BY CASE price."Format"
        WHEN 'PLATO' THEN 0 WHEN 'UNIDAD' THEN 0
        WHEN 'COPA' THEN 0 WHEN 'RACION' THEN 0 ELSE 1 END,
        price."Format"
    LIMIT 1
) default_price
WHERE p."Id" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                  AND 'b1000000-0000-4000-8000-000000000046'::uuid
AND NOT EXISTS (
    SELECT 1 FROM catalog_menu_entries e
    WHERE e."MenuId" = '11111111-1111-4111-8111-111111111111'::uuid
      AND e."ProductId" = p."Id"
);

UPDATE catalog_menu_entries e
SET "IsAvailable" = true,
    "DefaultFormat" = default_price."Format"
FROM products p
CROSS JOIN LATERAL (
    SELECT price."Format"
    FROM product_prices price
    WHERE price."ProductId" = p."Id"
      AND price."RestaurantId" = '00000000-0000-0000-0000-000000000000'::uuid
      AND price."Channel" = 'LOCAL'
    ORDER BY CASE price."Format"
        WHEN 'PLATO' THEN 0 WHEN 'UNIDAD' THEN 0
        WHEN 'COPA' THEN 0 WHEN 'RACION' THEN 0 ELSE 1 END,
        price."Format"
    LIMIT 1
) default_price
WHERE e."MenuId" = '11111111-1111-4111-8111-111111111111'::uuid
  AND e."ProductId" = p."Id"
  AND p."Id" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                  AND 'b1000000-0000-4000-8000-000000000046'::uuid;

DO $$
BEGIN
    IF EXISTS (
        SELECT price."ProductId", price."Channel"
        FROM product_prices price
        WHERE price."RestaurantId" = '00000000-0000-0000-0000-000000000000'::uuid
          AND price."ProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                                     AND 'b1000000-0000-4000-8000-000000000046'::uuid
        GROUP BY price."ProductId", price."Channel"
        HAVING count(DISTINCT price."Format") > 3
    ) THEN
        RAISE EXCEPTION 'Un producto del seed tiene más de tres formatos de precio.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM catalog_menu_entries entry
        WHERE entry."MenuId" = '11111111-1111-4111-8111-111111111111'::uuid
          AND entry."ProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                                     AND 'b1000000-0000-4000-8000-000000000046'::uuid
          AND NOT EXISTS (
              SELECT 1
              FROM catalog_menu_prices price
              WHERE price."MenuId" = entry."MenuId"
                AND price."ProductId" = entry."ProductId"
                AND price."Channel" = 'LOCAL'
                AND price."Format" = entry."DefaultFormat"
          )
    ) THEN
        RAISE EXCEPTION 'Un producto del seed no tiene precio para su formato predeterminado.';
    END IF;
END $$;

-- Los cinco locales de desarrollo operan con los productos de la carta general.
WITH seeded_locales("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
)
INSERT INTO restaurant_product_availability ("RestaurantId", "ProductId", "IsAvailable")
SELECT local."RestaurantId", product."Id", true
FROM seeded_locales local
CROSS JOIN products product
WHERE product."Id" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                       AND 'b1000000-0000-4000-8000-000000000046'::uuid
ON CONFLICT ("RestaurantId", "ProductId") DO UPDATE
SET "IsAvailable" = EXCLUDED."IsAvailable";

INSERT INTO restaurant_product_availability ("RestaurantId", "ProductId", "IsAvailable")
SELECT local."RestaurantId", product."Id", true
FROM (
    SELECT DISTINCT "RestaurantId"
    FROM restaurant_product_availability
    WHERE "IsAvailable" = true
) local
CROSS JOIN products product
WHERE product."Id" BETWEEN 'b1000000-0000-4000-8000-000000000038'::uuid
                       AND 'b1000000-0000-4000-8000-000000000046'::uuid
ON CONFLICT ("RestaurantId", "ProductId") DO NOTHING;

DELETE FROM product_allergens
WHERE "ProductId" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                       AND 'b1000000-0000-4000-8000-000000000037'::uuid;

WITH ranked AS (
    SELECT p."Id" AS "ProductId", a."Id" AS "AllergenId",
           row_number() OVER (
               PARTITION BY p."Id"
               ORDER BY md5(p."Id"::text || a."Id"::text)
           ) AS "Rank"
    FROM products p CROSS JOIN allergens a
    WHERE p."Id" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                      AND 'b1000000-0000-4000-8000-000000000037'::uuid
      AND a."IsActive" = true
)
INSERT INTO product_allergens ("ProductId", "AllergenId")
SELECT "ProductId", "AllergenId"
FROM ranked
WHERE "Rank" <= 3
ON CONFLICT ("ProductId", "AllergenId") DO UPDATE
SET "AllergenId" = EXCLUDED."AllergenId";

-- Las promociones heredan los alérgenos del plato anunciado.
DELETE FROM product_allergens
WHERE "ProductId" BETWEEN 'b1000000-0000-4000-8000-000000000044'::uuid
                       AND 'b1000000-0000-4000-8000-000000000046'::uuid;

WITH promoted("ProductId", "SourceProductId") AS (
    VALUES
        ('b1000000-0000-4000-8000-000000000044'::uuid, 'b1000000-0000-4000-8000-000000000014'::uuid),
        ('b1000000-0000-4000-8000-000000000045'::uuid, 'b1000000-0000-4000-8000-000000000001'::uuid),
        ('b1000000-0000-4000-8000-000000000046'::uuid, 'b1000000-0000-4000-8000-000000000008'::uuid)
), inherited AS (
    SELECT promoted."ProductId", allergen."AllergenId"
    FROM promoted
    JOIN product_allergens allergen ON allergen."ProductId" = promoted."SourceProductId"
)
INSERT INTO product_allergens ("ProductId", "AllergenId")
SELECT "ProductId", "AllergenId" FROM inherited
ON CONFLICT ("ProductId", "AllergenId") DO NOTHING;

WITH restaurants("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
), inserted AS (
    INSERT INTO restaurant_menu_items (
        "Id", "RestaurantId", "ProductId", "Price", "IsAvailable",
        "PreparationStationCode", "PreparationStationName", "Version", "UpdatedAtUtc"
    )
    SELECT gen_random_uuid(), r."RestaurantId", p."Id", pp."Amount",
           COALESCE(selection."IsAvailable", false),
           c."DefaultStationCode", c."DefaultStationName", 1, now()
    FROM restaurants r CROSS JOIN products p
    JOIN categories c ON c."Id" = p."CategoryId"
    JOIN catalog_menu_entries entry ON entry."MenuId" = '11111111-1111-4111-8111-111111111111'::uuid
        AND entry."ProductId" = p."Id"
    LEFT JOIN restaurant_product_availability selection
        ON selection."RestaurantId" = r."RestaurantId" AND selection."ProductId" = p."Id"
    JOIN product_prices pp ON pp."ProductId" = p."Id"
        AND pp."RestaurantId" = '00000000-0000-0000-0000-000000000000'::uuid
        AND pp."Channel" = 'LOCAL'
        AND pp."Format" = entry."DefaultFormat"
    WHERE p."Id" BETWEEN 'b1000000-0000-4000-8000-000000000001'::uuid
                      AND 'b1000000-0000-4000-8000-000000000046'::uuid
    ON CONFLICT ("RestaurantId", "ProductId") DO UPDATE SET
        "Price" = EXCLUDED."Price",
        "IsAvailable" = EXCLUDED."IsAvailable",
        "PreparationStationCode" = EXCLUDED."PreparationStationCode",
        "PreparationStationName" = EXCLUDED."PreparationStationName",
        "Version" = restaurant_menu_items."Version" + 1,
        "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.Events.CatalogItemChanged',
       jsonb_build_object(
           'RestaurantId', i."RestaurantId", 'ProductId', i."ProductId", 'Sku', p."Sku", 'ProductName', p."Name",
           'CategoryId', c."Id", 'CategoryCode', c."Code", 'CategoryName', c."Name", 'Price', i."Price",
           'IsAvailable', i."IsAvailable", 'PreparationStationCode', i."PreparationStationCode",
           'PreparationStationName', i."PreparationStationName", 'RequiresKds', c."RequiresKds",
           'TaxRate', (SELECT tax."Rate" FROM taxes tax WHERE tax."Id" = p."TaxId"), 'Version', i."Version",
           'OccurredAtUtc', to_char(i."UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
           'DefaultFormat', entry."DefaultFormat",
           'Formats', COALESCE((
               SELECT jsonb_agg(jsonb_build_object('Format', menu_price."Format", 'Price', menu_price."Amount")
                                ORDER BY menu_price."Format")
               FROM catalog_menu_prices menu_price
               WHERE menu_price."MenuId" = entry."MenuId"
                 AND menu_price."ProductId" = i."ProductId"
                 AND menu_price."Channel" = 'LOCAL'
           ), '[]'::jsonb),
           'ExtraGroups', COALESCE((
               SELECT jsonb_agg(
                   jsonb_build_object(
                       'Id', g."Id",
                       'Name', g."Name",
                       'MinSelections', g."MinSelections",
                       'MaxSelections', g."MaxSelections",
                       'Options', COALESCE((
                           SELECT jsonb_agg(
                               jsonb_build_object(
                                   'ProductId', extra."Id",
                                   'Sku', extra."Sku",
                                   'ProductName', extra."Name",
                                   'Price', extra_price."Amount",
                                   'TaxRate', (SELECT tax."Rate" FROM taxes tax WHERE tax."Id" = extra."TaxId")
                               ) ORDER BY po."SortOrder"
                           )
                           FROM product_options po
                           JOIN products extra ON extra."Id" = po."ProductId"
                           JOIN catalog_menu_prices extra_price
                             ON extra_price."MenuId" = '11111111-1111-4111-8111-111111111111'::uuid
                            AND extra_price."ProductId" = extra."Id"
                            AND extra_price."Channel" = 'LOCAL'
                            AND extra_price."Format" = 'RACION'
                           WHERE po."GroupId" = g."Id"
                             AND extra."IsActive" = true
                             AND extra."TaxId" IS NOT NULL
                             AND EXISTS (
                                 SELECT 1 FROM taxes tax
                                 WHERE tax."Id" = extra."TaxId" AND tax."IsActive" = true
                             )
                       ), '[]'::jsonb)
                   ) ORDER BY g."SortOrder"
               )
               FROM product_option_groups g
               WHERE g."ParentProductId" = i."ProductId"
                 AND g."Kind" = 'Extra'
           ), '[]'::jsonb)
       ),
       i."UpdatedAtUtc"
FROM inserted i
JOIN products p ON p."Id" = i."ProductId"
JOIN categories c ON c."Id" = p."CategoryId"
JOIN catalog_menu_entries entry ON entry."MenuId" = '11111111-1111-4111-8111-111111111111'::uuid
    AND entry."ProductId" = i."ProductId";

-- Reemplaza las estaciones de los locales de desarrollo y propaga bajas/altas a la proyección.
WITH restaurants("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
), deleted AS (
    DELETE FROM restaurant_kitchen_stations station
    USING restaurants r
    WHERE station."RestaurantId" = r."RestaurantId"
    RETURNING station.*
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.Events.KitchenStationChanged',
       jsonb_build_object(
           'StationId', "Id", 'RestaurantId', "RestaurantId", 'Code', "Code", 'Name', "Name",
           'IsPrimary', false, 'RequiresPrimaryDispatch', false,
           'Priority', "Priority", 'IsActive', false, 'Version', "Version" + 1,
           'OccurredAtUtc', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
           'IsDeleted', true
       ),
       now()
FROM deleted;

WITH restaurants("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
), stations("Code", "Name", "IsPrimary", "RequiresPrimaryDispatch", "Priority") AS (
    VALUES
        ('CHEF', 'Vista completa', true, false, 0),
        ('ENTRANTES', 'Entrantes', false, true, 1),
        ('BEBIDAS', 'Bebidas', false, false, 1),
        ('PASTAS', 'Pastas', false, true, 2),
        ('CARNES', 'Carnes', false, true, 2),
        ('POSTRES', 'Postres', false, false, 3)
), inserted AS (
    INSERT INTO restaurant_kitchen_stations (
        "Id", "RestaurantId", "Code", "Name", "IsPrimary",
        "RequiresPrimaryDispatch", "Priority", "IsActive", "Version", "UpdatedAtUtc"
    )
    SELECT gen_random_uuid(), r."RestaurantId", s."Code", s."Name", s."IsPrimary",
           s."RequiresPrimaryDispatch", s."Priority", true, 1, now()
    FROM restaurants r CROSS JOIN stations s
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.Events.KitchenStationChanged',
       jsonb_build_object(
           'StationId', "Id", 'RestaurantId', "RestaurantId", 'Code', "Code", 'Name', "Name",
           'IsPrimary', "IsPrimary", 'RequiresPrimaryDispatch', "RequiresPrimaryDispatch",
           'Priority', "Priority", 'IsActive', "IsActive", 'Version', "Version",
           'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;


UPDATE outbox_messages
SET "Type" = CASE "Type"
        WHEN 'Restaurantes.Catalog.Contracts.CategoryChanged' THEN 'Restaurantes.Catalog.Contracts.Events.CategoryChanged'
        WHEN 'Restaurantes.Catalog.Contracts.ProductChanged' THEN 'Restaurantes.Catalog.Contracts.Events.ProductChanged'
        WHEN 'Restaurantes.Catalog.Contracts.CatalogItemChanged' THEN 'Restaurantes.Catalog.Contracts.Events.CatalogItemChanged'
        WHEN 'Restaurantes.Catalog.Contracts.KitchenStationChanged' THEN 'Restaurantes.Catalog.Contracts.Events.KitchenStationChanged'
    END
WHERE "Type" IN (
    'Restaurantes.Catalog.Contracts.CategoryChanged',
    'Restaurantes.Catalog.Contracts.ProductChanged',
    'Restaurantes.Catalog.Contracts.CatalogItemChanged',
    'Restaurantes.Catalog.Contracts.KitchenStationChanged'
);
\connect restaurants_operational
SET search_path TO dining;

-- Restaura el nombre de la zona del salón principal; sus ubicaciones se llaman Mesa.
UPDATE dining_zones old_zone
SET "Name" = 'SALÓN PRINCIPAL',
    "SortOrder" = 2
WHERE old_zone."Name" = 'MESA'
  AND old_zone."DeletedAtUtc" IS NULL
  AND NOT EXISTS (
      SELECT 1
      FROM dining_zones main_hall
      WHERE main_hall."RestaurantId" = old_zone."RestaurantId"
        AND main_hall."Name" = 'SALÓN PRINCIPAL'
        AND main_hall."DeletedAtUtc" IS NULL
  );

WITH restaurants("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
), zones("Name", "SortOrder", "RequestGuestCount") AS (
    VALUES
        ('TERRAZA', 0, true),
        ('BARRA', 1, false),
        ('SALÓN PRINCIPAL', 2, true)
)
INSERT INTO dining_zones ("Id", "RestaurantId", "Name", "SortOrder", "RequestGuestCount")
SELECT gen_random_uuid(), r."RestaurantId", z."Name", z."SortOrder", z."RequestGuestCount"
FROM restaurants r CROSS JOIN zones z
ON CONFLICT ("RestaurantId", "Name") WHERE "DeletedAtUtc" IS NULL DO UPDATE
SET "SortOrder" = EXCLUDED."SortOrder",
    "RequestGuestCount" = EXCLUDED."RequestGuestCount";

UPDATE restaurant_tables t
SET "Code" = 'SALON-PRINCIPAL-' || right(t."Code", 2),
    "Label" = 'Mesa ' || right(t."Code", 2)
FROM dining_zones z
WHERE t."RestaurantId" = z."RestaurantId"
  AND t."ZoneId" = z."Id"
  AND z."Name" = 'SALÓN PRINCIPAL'
  AND z."DeletedAtUtc" IS NULL
  AND (t."Code" LIKE 'MESA-%' OR t."Code" LIKE 'SALON-PRINCIPAL-%')
  AND t."DeletedAtUtc" IS NULL;

UPDATE restaurant_tables t
SET "Label" = 'Mesa ' || right(t."Code", 2)
FROM dining_zones z
WHERE t."RestaurantId" = z."RestaurantId"
  AND t."ZoneId" = z."Id"
  AND z."Name" = 'TERRAZA'
  AND z."DeletedAtUtc" IS NULL
  AND t."Code" LIKE 'TERRAZA-%'
  AND t."DeletedAtUtc" IS NULL;

WITH restaurants("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
), desired_zones("Name", "CodePrefix", "LabelPrefix", "TableCount") AS (
    VALUES
        ('TERRAZA', 'TERRAZA', 'Mesa', 10),
        ('BARRA', 'BARRA', 'Barra', 10),
        ('SALÓN PRINCIPAL', 'SALON-PRINCIPAL', 'Mesa', 25)
), restaurant_zones AS (
    SELECT z."RestaurantId", z."Id" AS "ZoneId", d."CodePrefix", d."LabelPrefix", d."TableCount"
    FROM dining_zones z
    JOIN restaurants r ON r."RestaurantId" = z."RestaurantId"
    JOIN desired_zones d ON d."Name" = z."Name"
    WHERE z."DeletedAtUtc" IS NULL
)
INSERT INTO restaurant_tables (
    "Id", "RestaurantId", "Code", "Label", "IsActive", "CreatedAtUtc",
    "QrCode", "ZoneId"
)
SELECT gen_random_uuid(), z."RestaurantId",
       z."CodePrefix" || '-' || to_char(n, 'FM00'),
       z."LabelPrefix" || ' ' || to_char(n, 'FM00'),
       true, now(), upper(replace(gen_random_uuid()::text, '-', '')),
       z."ZoneId"
FROM restaurant_zones z
CROSS JOIN LATERAL generate_series(1, z."TableCount") AS numbers(n)
ON CONFLICT ("RestaurantId", "Code") WHERE "DeletedAtUtc" IS NULL DO UPDATE SET
    "Label" = EXCLUDED."Label",
    "IsActive" = EXCLUDED."IsActive",
    "ZoneId" = EXCLUDED."ZoneId";

\connect restaurants_operational
SET search_path TO dining;

\set ON_ERROR_STOP on

\connect restaurant_operations_write

WITH inserted AS (
    INSERT INTO restaurants ("Id", "Code", "Name", "Address", "IsActive", "Version", "UpdatedAtUtc")
    VALUES
        ('11111111-1111-4111-8111-111111111111', 'MAD-CENTRO', 'Restaurante Madrid Centro', 'Calle Mayor 10, Madrid', true, 1, now()),
        ('22222222-2222-4222-8222-222222222222', 'MAD-NORTE', 'Restaurante Madrid Norte', 'Paseo de la Castellana 200, Madrid', true, 1, now()),
        ('33333333-3333-4333-8333-333333333333', 'BCN-CENTRO', 'Restaurante Barcelona Centro', 'Carrer de Mallorca 100, Barcelona', true, 1, now()),
        ('44444444-4444-4444-8444-444444444444', 'VAL-CENTRO', 'Restaurante Valencia Centro', 'Carrer de Colón 50, Valencia', true, 1, now()),
        ('55555555-5555-4555-8555-555555555555', 'SEV-CENTRO', 'Restaurante Sevilla Centro', 'Calle Sierpes 25, Sevilla', true, 1, now())
    ON CONFLICT ("Code") DO NOTHING
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.RestaurantOperations.Contracts.RestaurantChanged',
       jsonb_build_object(
           'RestaurantId', "Id", 'Code', "Code", 'Name', "Name", 'Address', "Address",
           'IsActive', "IsActive", 'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;

\connect catalog_write

WITH inserted AS (
    INSERT INTO categories ("Id", "Code", "Name", "DefaultStationCode", "DefaultStationName", "IsActive", "Version", "UpdatedAtUtc")
    VALUES
        ('a1000000-0000-4000-8000-000000000001', 'PASTAS', 'Pastas', 'PASTAS', 'Pastas', true, 1, now()),
        ('a1000000-0000-4000-8000-000000000002', 'BEBIDAS', 'Bebidas', 'BEBIDAS', 'Bebidas', true, 1, now()),
        ('a1000000-0000-4000-8000-000000000003', 'CARNES', 'Carnes', 'CARNES', 'Carnes', true, 1, now()),
        ('a1000000-0000-4000-8000-000000000004', 'POSTRES', 'Postres', 'POSTRES', 'Postres', true, 1, now()),
        ('10000000-0000-4000-8000-000000000001', 'ENTRANTES', 'Entrantes', 'ENTRANTES', 'Entrantes', true, 1, now())
    ON CONFLICT ("Code") DO NOTHING
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.CategoryChanged',
       jsonb_build_object(
           'CategoryId', "Id", 'Code', "Code", 'Name', "Name",
           'DefaultStationCode', "DefaultStationCode", 'DefaultStationName', "DefaultStationName",
           'IsActive', "IsActive", 'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;

WITH inserted AS (
    INSERT INTO products ("Id", "Sku", "Name", "CategoryId", "BasePrice", "IsActive", "Version", "UpdatedAtUtc")
    VALUES
        ('b1000000-0000-4000-8000-000000000001', 'PASTA-CARBONARA', 'Spaghetti carbonara', 'a1000000-0000-4000-8000-000000000001', 12.50, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000002', 'PASTA-BOLOGNESA', 'Tagliatelle boloñesa', 'a1000000-0000-4000-8000-000000000001', 13.00, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000003', 'LASANA-CASA', 'Lasaña de la casa', 'a1000000-0000-4000-8000-000000000001', 14.00, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000004', 'AGUA-50CL', 'Agua mineral 50 cl', 'a1000000-0000-4000-8000-000000000002', 2.20, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000005', 'COLA-33CL', 'Refresco de cola', 'a1000000-0000-4000-8000-000000000002', 3.25, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000006', 'CERVEZA-CASA', 'Cerveza de la casa', 'a1000000-0000-4000-8000-000000000002', 3.50, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000007', 'ENTRECOT-250', 'Entrecot 250 g', 'a1000000-0000-4000-8000-000000000003', 21.50, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000008', 'BURGER-CLASICA', 'Hamburguesa clásica', 'a1000000-0000-4000-8000-000000000003', 13.50, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000009', 'POLLO-PARRILLA', 'Pollo a la parrilla', 'a1000000-0000-4000-8000-000000000003', 15.00, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000010', 'TIRAMISU', 'Tiramisú', 'a1000000-0000-4000-8000-000000000004', 6.50, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000011', 'TARTA-QUESO', 'Tarta de queso', 'a1000000-0000-4000-8000-000000000004', 6.80, true, 1, now()),
        ('b1000000-0000-4000-8000-000000000012', 'HELADO-VAINILLA', 'Helado de vainilla', 'a1000000-0000-4000-8000-000000000004', 5.50, true, 1, now())
    ON CONFLICT ("Sku") DO NOTHING
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.ProductChanged',
       jsonb_build_object(
           'ProductId', "Id", 'Sku', "Sku", 'Name', "Name", 'CategoryId', "CategoryId",
           'BasePrice', "BasePrice", 'IsActive', "IsActive", 'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;

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
    SELECT md5(r."RestaurantId"::text || ':' || s."Code")::uuid,
           r."RestaurantId", s."Code", s."Name", s."IsPrimary",
           s."RequiresPrimaryDispatch", s."Priority", true, 1, now()
    FROM restaurants r CROSS JOIN stations s
    ON CONFLICT ("RestaurantId", "Code") DO NOTHING
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.KitchenStationChanged',
       jsonb_build_object(
           'StationId', "Id", 'RestaurantId', "RestaurantId", 'Code', "Code", 'Name', "Name",
           'IsPrimary', "IsPrimary", 'RequiresPrimaryDispatch', "RequiresPrimaryDispatch",
           'Priority', "Priority",
           'IsActive', "IsActive", 'Version', "Version", 'OccurredAtUtc', to_char("UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       "UpdatedAtUtc"
FROM inserted;

WITH restaurants("RestaurantId", "Adjustment") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid, 0.00::numeric),
        ('22222222-2222-4222-8222-222222222222'::uuid, 0.50::numeric),
        ('33333333-3333-4333-8333-333333333333'::uuid, 1.00::numeric),
        ('44444444-4444-4444-8444-444444444444'::uuid, 0.75::numeric),
        ('55555555-5555-4555-8555-555555555555'::uuid, 0.25::numeric)
), inserted AS (
    INSERT INTO restaurant_menu_items (
        "Id", "RestaurantId", "ProductId", "Price", "IsAvailable",
        "PreparationStationCode", "PreparationStationName", "Version", "UpdatedAtUtc"
    )
    SELECT gen_random_uuid(), r."RestaurantId", p."Id", p."BasePrice" + r."Adjustment",
           NOT (r."RestaurantId" = '33333333-3333-4333-8333-333333333333' AND p."Sku" = 'LASANA-CASA'),
           c."DefaultStationCode", c."DefaultStationName", 1, now()
    FROM restaurants r CROSS JOIN products p JOIN categories c ON c."Id" = p."CategoryId"
    WHERE p."Sku" IN (
        'PASTA-CARBONARA', 'PASTA-BOLOGNESA', 'LASANA-CASA', 'AGUA-50CL', 'COLA-33CL', 'CERVEZA-CASA',
        'ENTRECOT-250', 'BURGER-CLASICA', 'POLLO-PARRILLA', 'TIRAMISU', 'TARTA-QUESO', 'HELADO-VAINILLA'
    )
    ON CONFLICT ("RestaurantId", "ProductId") DO NOTHING
    RETURNING *
)
INSERT INTO outbox_messages ("Id", "Type", "Payload", "OccurredAtUtc")
SELECT gen_random_uuid(),
       'Restaurantes.Catalog.Contracts.CatalogItemChanged',
       jsonb_build_object(
           'RestaurantId', i."RestaurantId", 'ProductId', i."ProductId", 'Sku', p."Sku", 'ProductName', p."Name",
           'CategoryId', c."Id", 'CategoryCode', c."Code", 'CategoryName', c."Name", 'Price', i."Price",
           'IsAvailable', i."IsAvailable", 'PreparationStationCode', i."PreparationStationCode",
           'PreparationStationName', i."PreparationStationName", 'Version', i."Version", 'OccurredAtUtc', to_char(i."UpdatedAtUtc" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
       ),
       i."UpdatedAtUtc"
FROM inserted i
JOIN products p ON p."Id" = i."ProductId"
JOIN categories c ON c."Id" = p."CategoryId";

\connect dining_write

WITH restaurants("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
)
INSERT INTO dining_zones ("Id", "RestaurantId", "Name", "SortOrder")
SELECT gen_random_uuid(), r."RestaurantId", 'General', 0 FROM restaurants r
WHERE NOT EXISTS (SELECT 1 FROM dining_zones z WHERE z."RestaurantId" = r."RestaurantId" AND z."Name" = 'General')
ON CONFLICT ("RestaurantId", "Name") WHERE "DeletedAtUtc" IS NULL DO NOTHING;

WITH restaurants("RestaurantId") AS (
    VALUES
        ('11111111-1111-4111-8111-111111111111'::uuid),
        ('22222222-2222-4222-8222-222222222222'::uuid),
        ('33333333-3333-4333-8333-333333333333'::uuid),
        ('44444444-4444-4444-8444-444444444444'::uuid),
        ('55555555-5555-4555-8555-555555555555'::uuid)
), locations AS (
    SELECT 'MESA-' || to_char(n, 'FM00') AS "Code", 'Mesa ' || to_char(n, 'FM00') AS "Label"
    FROM generate_series(1, 10) n
    UNION ALL
    SELECT 'BARRA-' || to_char(n, 'FM00'), 'Barra ' || to_char(n, 'FM00')
    FROM generate_series(1, 5) n
)
INSERT INTO restaurant_tables ("Id", "RestaurantId", "Code", "Label", "IsActive", "CreatedAtUtc", "QrCode", "ZoneId")
SELECT gen_random_uuid(), r."RestaurantId", l."Code", l."Label", true, now(),
       upper(md5(r."RestaurantId"::text || ':' || l."Code")), z."Id"
FROM restaurants r CROSS JOIN locations l
JOIN dining_zones z ON z."RestaurantId" = r."RestaurantId" AND z."Name" = 'General' AND z."DeletedAtUtc" IS NULL
WHERE NOT EXISTS (
    SELECT 1 FROM restaurant_tables existing
    WHERE existing."RestaurantId" = r."RestaurantId" AND existing."Code" = l."Code"
)
ON CONFLICT ("RestaurantId", "Code") WHERE "DeletedAtUtc" IS NULL DO NOTHING;

INSERT INTO dining_restaurant_policies (
    "RestaurantId", "UpdatedAtUtc", "Version", "QrAllowedNetworks",
    "RequireTrustedNetworkForQr", "TakeawayRequiresPrepayment", "QrRequiresImmediatePayment"
)
VALUES
    ('11111111-1111-4111-8111-111111111111', now(), 1, '', false, true, true),
    ('22222222-2222-4222-8222-222222222222', now(), 1, '', false, true, true),
    ('33333333-3333-4333-8333-333333333333', now(), 1, '', false, true, true),
    ('44444444-4444-4444-8444-444444444444', now(), 1, '', false, true, true),
    ('55555555-5555-4555-8555-555555555555', now(), 1, '', false, true, true)
ON CONFLICT ("RestaurantId") DO NOTHING;

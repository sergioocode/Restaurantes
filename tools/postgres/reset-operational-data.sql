-- Reinicia los datos operativos locales de desarrollo en las dos instancias PostgreSQL.
-- Conserva Identity, locales, mesas, zonas y configuración; reconstruye el catálogo con el seed.
--
-- Antes de empezar, detén todos los servicios y workers de la aplicación.
-- Para limpiar las dos bases y purgar RabbitMQ en una sola operación, desde la raíz
-- del repositorio ejecuta en PowerShell (si la ejecución de scripts está bloqueada,
-- el primer comando solo la habilita durante esta sesión):
--   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
--   .\tools\postgres\reset-operational-data.ps1 -ConfirmReset BORRAR
-- El orquestador ejecuta este archivo para operational y analytics y después purga
-- las colas de Catalog, Dining, Orders, Payments, Sales y Reporting, incluidas las retry y DLQ.
-- No purga colas de RestaurantOperations porque sus datos se conservan.
-- La parte SQL de este archivo solo limpia PostgreSQL; RabbitMQ requiere Docker CLI.
--
-- Ejecuta cada modo contra su instancia indicada. El script verifica tanto el modo
-- solicitado como current_database() para evitar limpiar la instancia equivocada.
-- Antes de truncar también comprueba que estén aplicadas las migraciones de todas
-- las tablas operativas/read models incluidas en cada modo.

\set ON_ERROR_STOP on

\if :{?confirm_reset}
\else
    \echo 'Abortado: falta -v confirm_reset=BORRAR.'
    \quit 3
\endif

\if :{?reset_target}
\else
    \echo 'Abortado: falta -v reset_target=operational o -v reset_target=analytics.'
    \quit 3
\endif

SELECT :'confirm_reset' = 'BORRAR' AS confirmed \gset
\if :confirmed
    \echo 'Confirmación recibida.'
\else
    \echo 'Abortado: confirm_reset debe ser BORRAR.'
    \quit 3
\endif

SELECT :'reset_target' IN ('operational', 'analytics') AS valid_target \gset
\if :valid_target
\else
    \echo 'Abortado: reset_target debe ser operational o analytics.'
    \quit 3
\endif

SELECT current_database() = CASE :'reset_target'
    WHEN 'operational' THEN 'restaurants_operational'
    WHEN 'analytics' THEN 'restaurants_analytics'
END AS correct_database \gset
\if :correct_database
    \echo 'Base de datos verificada:' :reset_target
\else
    \echo 'Abortado: reset_target no coincide con la base de datos conectada.'
    \quit 3
\endif

SELECT :'reset_target' = 'operational' AS reset_operational \gset
\if :reset_operational
    DO $$
    DECLARE
        missing_tables text;
    BEGIN
        SELECT string_agg(table_name, ', ' ORDER BY table_name)
        INTO missing_tables
        FROM unnest(ARRAY[
            'orders_write.order_lines',
            'orders_write.order_kitchen_stations',
            'orders_write.order_payments',
            'orders_write.orders',
            'orders_write.outbox_messages',
            'dining.pending_payments',
            'dining.dining_session_orders',
            'dining.dining_sessions',
            'dining.inbox_messages',
            'payments.payable_orders',
            'payments.payment_captures',
            'payments.inbox_messages',
            'payments.outbox_messages',
            'cash_register.cash_movements',
            'cash_register.cash_register_reconciliations',
            'cash_register.cash_register_sessions',
            'cash_register.inbox_messages',
            'sales_write.sale_orders',
            'sales_write.sale_payments',
            'sales_write.sales',
            'sales_write.delivered_order_markers',
            'sales_write.inbox_messages',
            'sales_write.outbox_messages',
            'catalog_write.categories',
            'catalog_write.products',
            'catalog_write.restaurant_menu_items',
            'catalog_write.restaurant_kitchen_stations',
            'catalog_write.outbox_messages',
            'catalog_write.taxes',
            'catalog_write.allergens',
            'catalog_write.product_allergens',
            'catalog_write.product_photos',
            'catalog_write.product_configurations',
            'catalog_write.product_option_groups',
            'catalog_write.product_options',
            'catalog_write.product_prices',
            'catalog_write.catalog_menus',
            'catalog_write.restaurant_product_availability',
            'catalog_write.catalog_menu_prices',
            'catalog_write.catalog_menu_entries',
            'catalog_write.catalog_menu_channels'
        ]) AS expected(table_name)
        WHERE to_regclass(table_name) IS NULL;

        IF missing_tables IS NOT NULL THEN
            RAISE EXCEPTION 'Faltan tablas operativas; aplica las migraciones antes del reset: %', missing_tables;
        END IF;

    END
    $$;

    BEGIN;
    TRUNCATE TABLE
        orders_write.order_lines,
        orders_write.order_kitchen_stations,
        orders_write.order_payments,
        orders_write.orders,
        orders_write.outbox_messages,
        dining.pending_payments,
        dining.dining_session_orders,
        dining.dining_sessions,
        dining.inbox_messages,
        payments.payment_captures,
        payments.payable_orders,
        payments.inbox_messages,
        payments.outbox_messages,
        cash_register.cash_movements,
        cash_register.cash_register_reconciliations,
        cash_register.cash_register_sessions,
        cash_register.inbox_messages,
        sales_write.sale_orders,
        sales_write.sale_payments,
        sales_write.sales,
        sales_write.delivered_order_markers,
        sales_write.inbox_messages,
        sales_write.outbox_messages,
        catalog_write.categories,
        catalog_write.products,
        catalog_write.restaurant_menu_items,
        catalog_write.restaurant_kitchen_stations,
        catalog_write.outbox_messages,
        catalog_write.taxes,
        catalog_write.allergens,
        catalog_write.product_allergens,
        catalog_write.product_photos,
        catalog_write.product_configurations,
        catalog_write.product_option_groups,
        catalog_write.product_options,
        catalog_write.product_prices,
        catalog_write.catalog_menus,
        catalog_write.restaurant_product_availability,
        catalog_write.catalog_menu_prices,
        catalog_write.catalog_menu_entries,
        catalog_write.catalog_menu_channels
    RESTART IDENTITY;
    COMMIT;
\endif

SELECT :'reset_target' = 'analytics' AS reset_analytics \gset
\if :reset_analytics
    DO $$
    DECLARE
        missing_tables text;
    BEGIN
        SELECT string_agg(table_name, ', ' ORDER BY table_name)
        INTO missing_tables
        FROM unnest(ARRAY[
            'orders_read.kitchen_order_views',
            'orders_read.inbox_messages',
            'sales_read.sales',
            'sales_read.inbox_messages',
            'reporting.order_reporting_facts',
            'reporting.completed_sale_facts',
            'reporting.inbox_messages',
            'reporting.outbox_messages',
            'catalog_read.category_views',
            'catalog_read.product_views',
            'catalog_read.menu_item_views',
            'catalog_read.kitchen_station_views',
            'catalog_read.inbox_messages'
        ]) AS expected(table_name)
        WHERE to_regclass(table_name) IS NULL;

        IF missing_tables IS NOT NULL THEN
            RAISE EXCEPTION 'Faltan tablas analíticas; aplica las migraciones antes del reset: %', missing_tables;
        END IF;
    END
    $$;

    BEGIN;
    TRUNCATE TABLE
        orders_read.kitchen_order_views,
        orders_read.inbox_messages,
        sales_read.sales,
        sales_read.inbox_messages,
        reporting.order_reporting_facts,
        reporting.completed_sale_facts,
        reporting.inbox_messages,
        reporting.outbox_messages,
        catalog_read.category_views,
        catalog_read.product_views,
        catalog_read.menu_item_views,
        catalog_read.kitchen_station_views,
        catalog_read.inbox_messages
    RESTART IDENTITY;
    COMMIT;
\endif

\echo 'Limpieza completada. Identity y configuración se conservaron; ejecuta el seed para reconstruir el catálogo.'

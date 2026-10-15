using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurantes.Catalog.Infrastructure.Persistence.Write.Migrations;

public partial class AddAllergenDescriptionAndSeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE allergens ADD COLUMN IF NOT EXISTS "Description" character varying(500) NOT NULL DEFAULT '';
            ALTER TABLE allergens ALTER COLUMN "Description" DROP DEFAULT;

            INSERT INTO allergens ("Id", "Code", "Name", "Description", "IsActive") VALUES
              ('00000000-0000-0000-0000-00000000a001', 'GLU', 'Gluten', 'Cereales con gluten (trigo, centeno, cebada, avena, espelta) y sus derivados', TRUE),
              ('00000000-0000-0000-0000-00000000a002', 'CRU', 'Crustáceos', 'Gambas, langostinos, cangrejos, cigalas, nécoras y derivados', TRUE),
              ('00000000-0000-0000-0000-00000000a003', 'HUE', 'Huevos', 'Huevos y productos a base de huevo (mayonesas, masas, empanados)', TRUE),
              ('00000000-0000-0000-0000-00000000a004', 'PES', 'Pescado', 'Pescado y productos a base de pescado (caldos, salsas, gelatinas)', TRUE),
              ('00000000-0000-0000-0000-00000000a005', 'CAC', 'Cacahuetes', 'Cacahuetes, mantecas, aceites y productos derivados', TRUE),
              ('00000000-0000-0000-0000-00000000a006', 'SOJ', 'Soja', 'Soja y productos a base de soja (tofu, salsa de soja, lecitina)', TRUE),
              ('00000000-0000-0000-0000-00000000a007', 'LAC', 'Lácteos', 'Leche y sus derivados, incluida la lactosa (queso, nata, mantequilla, yogur)', TRUE),
              ('00000000-0000-0000-0000-00000000a008', 'FRU', 'Frutos secos', 'Frutos de cáscara (almendras, avellanas, nueces, anacardos, pistachos)', TRUE),
              ('00000000-0000-0000-0000-00000000a009', 'API', 'Apio', 'Apio y productos derivados (hojas, semillas, en polvo)', TRUE),
              ('00000000-0000-0000-0000-00000000a00a', 'MOS', 'Mostaza', 'Mostaza y productos derivados (semillas, salsas, aderezos)', TRUE),
              ('00000000-0000-0000-0000-00000000a00b', 'SES', 'Sésamo', 'Granos de sésamo y productos a base de sésamo (tahini, aceites)', TRUE),
              ('00000000-0000-0000-0000-00000000a00c', 'SUL', 'Sulfitos', 'Dióxido de azufre y sulfitos en concentraciones superiores a 10 mg/kg o 10 mg/l (vinos, vinagres)', TRUE),
              ('00000000-0000-0000-0000-00000000a00d', 'ALT', 'Altramuces', 'Altramuces y productos a base de altramuces', TRUE),
              ('00000000-0000-0000-0000-00000000a00e', 'MOL', 'Moluscos', 'Moluscos y productos a base de moluscos (mejillones, calamares, pulpo)', TRUE)
            ON CONFLICT ("Code") DO UPDATE
            SET "Description" = EXCLUDED."Description"
            WHERE allergens."Description" = '';
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE allergens DROP COLUMN IF EXISTS \"Description\";");
    }
}

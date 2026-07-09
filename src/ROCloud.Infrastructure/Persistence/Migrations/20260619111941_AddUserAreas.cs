using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROCloud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_areas", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_areas_areas_area_id",
                        column: x => x.area_id,
                        principalTable: "areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_areas_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_areas_area_id",
                table: "user_areas",
                column: "area_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_areas_tenant_id_area_id",
                table: "user_areas",
                columns: new[] { "tenant_id", "area_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_areas_user_id_area_id",
                table: "user_areas",
                columns: new[] { "user_id", "area_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_areas");
        }
    }
}

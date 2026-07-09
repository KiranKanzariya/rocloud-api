using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ROCloud.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAmcSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "amc_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    interval_months = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_service_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_amc_subscriptions", x => x.id);
                    table.CheckConstraint("ck_amc_subscriptions_interval", "interval_months IN (3, 6, 12)");
                    table.ForeignKey(
                        name: "FK_amc_subscriptions_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_amc_subscriptions_customer_id",
                table: "amc_subscriptions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_amc_subscriptions_tenant_id_customer_id",
                table: "amc_subscriptions",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_amc_subscriptions_tenant_id_next_due_date",
                table: "amc_subscriptions",
                columns: new[] { "tenant_id", "next_due_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "amc_subscriptions");
        }
    }
}

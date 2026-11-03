using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    complaint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    mileage_km_at_intake = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_total = table.Column<decimal>(type: "numeric(15,2)", precision: 15, scale: 2, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invoiced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                    table.CheckConstraint("ck_work_orders_mileage_km_at_intake_non_negative", "mileage_km_at_intake >= 0");
                    table.CheckConstraint("ck_work_orders_status", "status IN ('Draft', 'Approved', 'InProgress', 'Completed', 'Invoiced', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_work_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    part_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_lines", x => x.id);
                    table.CheckConstraint("ck_work_order_lines_kind", "kind IN ('Labour', 'Part')");
                    table.CheckConstraint("ck_work_order_lines_number_positive", "number > 0");
                    table.CheckConstraint("ck_work_order_lines_part_number_only_for_parts", "kind = 'Part' OR part_number IS NULL");
                    table.CheckConstraint("ck_work_order_lines_quantity_positive", "quantity > 0");
                    table.CheckConstraint("ck_work_order_lines_unit_price_non_negative", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_work_order_lines_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_work_order_lines_work_order_id_number",
                table: "work_order_lines",
                columns: new[] { "work_order_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_customer_id",
                table: "work_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_vehicle_id",
                table: "work_orders",
                column: "vehicle_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_lines");

            migrationBuilder.DropTable(
                name: "work_orders");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bay_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    booked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rescheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    no_show_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    slot_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    slot_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.CheckConstraint("ck_appointments_checked_in_has_work_order", "status <> 'CheckedIn' OR work_order_id IS NOT NULL");
                    table.CheckConstraint("ck_appointments_slot_ends_after_start", "slot_end > slot_start");
                    table.CheckConstraint("ck_appointments_status", "status IN ('Scheduled', 'CheckedIn', 'Cancelled', 'NoShow')");
                    table.ForeignKey(
                        name: "fk_appointments_bays_bay_id",
                        column: x => x.bay_id,
                        principalTable: "bays",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_bay_id",
                table: "appointments",
                column: "bay_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_customer_id",
                table: "appointments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_vehicle_id",
                table: "appointments",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ux_appointments_work_order_id",
                table: "appointments",
                column: "work_order_id",
                unique: true);

            // Added by hand after generation (ADR 0015): EF Core cannot model exclusion
            // constraints. No two appointments that hold their bay may overlap in the
            // same bay or for the same vehicle; '[)' matches AppointmentSlot.Overlaps.
            migrationBuilder.Sql(
                """
                ALTER TABLE appointments
                    ADD CONSTRAINT ex_appointments_bay_id_slot EXCLUDE USING gist (
                        bay_id WITH =,
                        tstzrange(slot_start, slot_end, '[)') WITH &&)
                    WHERE (status IN ('Scheduled', 'CheckedIn'));
                """);
            migrationBuilder.Sql(
                """
                ALTER TABLE appointments
                    ADD CONSTRAINT ex_appointments_vehicle_id_slot EXCLUDE USING gist (
                        vehicle_id WITH =,
                        tstzrange(slot_start, slot_end, '[)') WITH &&)
                    WHERE (status IN ('Scheduled', 'CheckedIn'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
        }
    }
}

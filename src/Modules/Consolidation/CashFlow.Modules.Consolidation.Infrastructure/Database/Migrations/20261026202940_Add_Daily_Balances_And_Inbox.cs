using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CashFlow.Modules.Consolidation.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class Add_Daily_Balances_And_Inbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consolidation");

            migrationBuilder.CreateTable(
                name: "daily_balances",
                schema: "consolidation",
                columns: table => new
                {
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    credits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    debits = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_balances", x => x.business_date);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "consolidation",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.event_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_balances",
                schema: "consolidation");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "consolidation");
        }
    }
}

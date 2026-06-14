using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessagingChannelLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingChannelLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessagingLinkCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingLinkCodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingChannelLinks_Channel_ExternalId",
                table: "MessagingChannelLinks",
                columns: new[] { "Channel", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessagingChannelLinks_UserId_Channel",
                table: "MessagingChannelLinks",
                columns: new[] { "UserId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingLinkCodes_CodeHash",
                table: "MessagingLinkCodes",
                column: "CodeHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessagingChannelLinks");

            migrationBuilder.DropTable(
                name: "MessagingLinkCodes");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Petty.Server.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class FriendshipsAndBlocks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Friendships",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LowerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                HigherUserId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Friendships", x => x.Id);
                table.CheckConstraint("CK_Friendships_CanonicalPair", "\"LowerUserId\" < \"HigherUserId\"");
                table.CheckConstraint("CK_Friendships_Requester", "\"RequestedById\" IN (\"LowerUserId\", \"HigherUserId\")");
                table.CheckConstraint("CK_Friendships_Status", "\"Status\" BETWEEN 0 AND 2 AND (\"Status\" = 0 OR \"RequestId\" IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_Friendships_AspNetUsers_HigherUserId",
                    column: x => x.HigherUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Friendships_AspNetUsers_LowerUserId",
                    column: x => x.LowerUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Friendships_AspNetUsers_RequestedById",
                    column: x => x.RequestedById,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserBlocks",
            columns: table => new
            {
                BlockerId = table.Column<Guid>(type: "uuid", nullable: false),
                BlockedId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerId, x.BlockedId });
                table.CheckConstraint("CK_UserBlocks_DifferentUsers", "\"BlockerId\" <> \"BlockedId\"");
                table.ForeignKey(
                    name: "FK_UserBlocks_AspNetUsers_BlockedId",
                    column: x => x.BlockedId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserBlocks_AspNetUsers_BlockerId",
                    column: x => x.BlockerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Friendships_HigherUserId",
            table: "Friendships",
            column: "HigherUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Friendships_LowerUserId_HigherUserId",
            table: "Friendships",
            columns: new[] { "LowerUserId", "HigherUserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Friendships_RequestedById",
            table: "Friendships",
            column: "RequestedById");

        migrationBuilder.CreateIndex(
            name: "IX_Friendships_RequestId",
            table: "Friendships",
            column: "RequestId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserBlocks_BlockedId",
            table: "UserBlocks",
            column: "BlockedId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Friendships");

        migrationBuilder.DropTable(
            name: "UserBlocks");
    }
}

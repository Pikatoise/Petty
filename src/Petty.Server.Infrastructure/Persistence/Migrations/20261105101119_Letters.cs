using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Petty.Server.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class Letters : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Letters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                Body = table.Column<string>(type: "text", nullable: false),
                SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Letters", x => x.Id);
                table.CheckConstraint("CK_Letters_BodyLength", "char_length(\"Body\") BETWEEN 1 AND 500");
                table.CheckConstraint("CK_Letters_ClientRequestId", "\"ClientRequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("CK_Letters_DifferentUsers", "\"SenderId\" <> \"RecipientId\"");
                table.ForeignKey(
                    name: "FK_Letters_AspNetUsers_RecipientId",
                    column: x => x.RecipientId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Letters_AspNetUsers_SenderId",
                    column: x => x.SenderId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Letters_RecipientId_ReadAtUtc_Id",
            table: "Letters",
            columns: new[] { "RecipientId", "ReadAtUtc", "Id" },
            descending: new[] { false, true, true });

        migrationBuilder.CreateIndex(
            name: "IX_Letters_RecipientId_SentAtUtc_Id",
            table: "Letters",
            columns: new[] { "RecipientId", "SentAtUtc", "Id" },
            filter: "\"ReadAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Letters_SenderId_ClientRequestId",
            table: "Letters",
            columns: new[] { "SenderId", "ClientRequestId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Letters_SenderId_RecipientId",
            table: "Letters",
            columns: new[] { "SenderId", "RecipientId" },
            filter: "\"ReadAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Letters_SenderId_SentAtUtc_Id",
            table: "Letters",
            columns: new[] { "SenderId", "SentAtUtc", "Id" },
            descending: new[] { false, true, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Letters");
    }
}

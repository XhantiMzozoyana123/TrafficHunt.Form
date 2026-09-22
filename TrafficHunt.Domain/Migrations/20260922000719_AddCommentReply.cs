using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrafficHunt.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentReply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "Comments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyText",
                table: "Comments",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "ReplyText",
                table: "Comments");
        }
    }
}

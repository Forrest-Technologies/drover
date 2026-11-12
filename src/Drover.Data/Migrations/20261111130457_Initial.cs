using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Roles and the sys schema come from DatabaseBootstrap, which runs before this migration.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION sys.current_space() RETURNS uuid LANGUAGE sql STABLE
                  AS $$ SELECT nullif(current_setting('drover.space_id', true), '')::uuid $$;

                CREATE FUNCTION sys.current_user_id() RETURNS uuid LANGUAGE sql STABLE
                  AS $$ SELECT nullif(current_setting('drover.user_id', true), '')::uuid $$;

                CREATE FUNCTION sys.everyone() RETURNS uuid LANGUAGE sql IMMUTABLE
                  AS $$ SELECT '00000000-0000-0000-0000-000000000000'::uuid $$;

                REVOKE EXECUTE ON FUNCTION sys.current_space(), sys.current_user_id(), sys.everyone() FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION sys.current_space(), sys.current_user_id(), sys.everyone() TO drover_app;
                """);

            migrationBuilder.CreateTable(
                name: "spaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spaces", x => x.id);
                    table.CheckConstraint("ck_spaces_kind", "kind IN ('personal', 'shared')");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => x.id);
                    table.CheckConstraint("ck_memberships_role", "role IN ('owner', 'admin', 'member')");
                    table.ForeignKey(
                        name: "fk_memberships_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_memberships_space_id_user_id",
                table: "memberships",
                columns: new[] { "space_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_slug",
                table: "spaces",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            // Policies. Each special case is written out here and covered by a named test.
            migrationBuilder.IsolateBySpace("memberships");
            migrationBuilder.Sql(
                """
                CREATE POLICY own_memberships ON memberships FOR SELECT
                  USING (user_id = sys.current_user_id());
                """);

            migrationBuilder.EnableForcedSecurity("spaces");
            migrationBuilder.Sql(
                """
                CREATE POLICY space_isolation ON spaces FOR ALL
                  USING (id = sys.current_space())
                  WITH CHECK (id = sys.current_space());
                GRANT SELECT, INSERT, UPDATE, DELETE ON spaces TO drover_app;
                """);

            migrationBuilder.EnableForcedSecurity("users");
            migrationBuilder.Sql(
                """
                CREATE POLICY user_visibility ON users FOR SELECT
                  USING (id = sys.current_user_id());
                CREATE POLICY user_insert_self ON users FOR INSERT
                  WITH CHECK (id = sys.current_user_id());
                CREATE POLICY user_update_self ON users FOR UPDATE
                  USING (id = sys.current_user_id())
                  WITH CHECK (id = sys.current_user_id());
                GRANT SELECT, INSERT, UPDATE ON users TO drover_app;
                """);

            migrationBuilder.EnableForcedSecurity("user_profiles");
            migrationBuilder.Sql(
                """
                CREATE POLICY profile_self ON user_profiles FOR ALL
                  USING (user_id = sys.current_user_id())
                  WITH CHECK (user_id = sys.current_user_id());
                GRANT SELECT, INSERT, UPDATE ON user_profiles TO drover_app;
                CREATE POLICY member_profiles ON user_profiles FOR SELECT
                  USING (
                    sys.current_user_id() IS NOT NULL
                    AND EXISTS (
                      SELECT 1 FROM memberships AS caller
                      WHERE caller.user_id = sys.current_user_id()
                        AND caller.space_id = sys.current_space())
                    AND EXISTS (
                      SELECT 1 FROM memberships AS member
                      WHERE member.user_id = user_profiles.user_id
                        AND member.space_id = sys.current_space()));
                """);

            migrationBuilder.IsolateByUser("user_claims");
            migrationBuilder.IsolateByUser("user_logins");
            migrationBuilder.IsolateByUser("user_tokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "user_profiles");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "spaces");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.Sql("DROP FUNCTION sys.everyone(), sys.current_user_id(), sys.current_space();");
        }
    }
}

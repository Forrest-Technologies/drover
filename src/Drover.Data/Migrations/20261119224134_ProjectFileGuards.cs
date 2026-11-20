using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Drover.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProjectFileGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_byte_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_byte_reservations", x => x.id);
                    table.CheckConstraint("ck_file_byte_reservations_bytes", "bytes > 0");
                });

            migrationBuilder.CreateTable(
                name: "pending_file_deletions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    cleanup_claim_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_file_deletions", x => x.id);
                    table.CheckConstraint("ck_pending_file_deletions_size", "size >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_file_deletions_space_id_project_id",
                table: "pending_file_deletions",
                columns: new[] { "space_id", "project_id" });

            migrationBuilder.IsolateBySpace("file_byte_reservations");
            migrationBuilder.IsolateBySpace("pending_file_deletions");
            migrationBuilder.Sql("REVOKE UPDATE ON pending_file_deletions FROM drover_app;");
            migrationBuilder.Sql("GRANT UPDATE (id, cleanup_claim_until) ON pending_file_deletions TO drover_app;");

            // A project file is shared with every member, so the audience policy would let a member change it.
            // Owners and admins are the only writers, and a delete returns quota and records the stored bytes in
            // the same statement. The function is invoker-rights: it sees only the caller's space.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION public.enforce_project_file_access()
                RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog, public, pg_temp
                AS $function$
                DECLARE
                  project uuid;
                BEGIN
                  IF TG_OP = 'DELETE' THEN
                    project := OLD.project_id;
                  ELSIF TG_OP = 'UPDATE' AND OLD.project_id IS NOT NULL THEN
                    project := OLD.project_id;
                  ELSE
                    project := NEW.project_id;
                  END IF;

                  IF project IS NULL THEN
                    IF TG_OP = 'DELETE' THEN
                      RETURN OLD;
                    END IF;
                    RETURN NEW;
                  END IF;

                  IF current_user <> 'drover_system' AND NOT EXISTS (
                    SELECT 1
                    FROM public.memberships AS member
                    WHERE member.space_id = sys.current_space()
                      AND member.user_id = sys.current_user_id()
                      AND member.role IN ('owner', 'admin')) THEN
                    RAISE EXCEPTION 'Only space owners and admins may change project files.'
                      USING ERRCODE = 'insufficient_privilege';
                  END IF;

                  IF TG_OP = 'DELETE' THEN
                    INSERT INTO public.pending_file_deletions (id, space_id, project_id, storage_key, size)
                    VALUES (
                      OLD.id,
                      OLD.space_id,
                      OLD.project_id,
                      replace(OLD.space_id::text, '-', '') || '/' || replace(OLD.id::text, '-', ''),
                      OLD.size);

                    UPDATE public.spaces
                    SET stored_file_bytes = CASE
                      WHEN stored_file_bytes > OLD.size THEN stored_file_bytes - OLD.size
                      ELSE 0
                    END
                    WHERE id = OLD.space_id;

                    RETURN OLD;
                  END IF;

                  RETURN NEW;
                END;
                $function$;

                REVOKE ALL ON FUNCTION public.enforce_project_file_access() FROM PUBLIC;

                CREATE TRIGGER trg_files_project_insert
                  BEFORE INSERT ON public.files
                  FOR EACH ROW
                  WHEN (NEW.project_id IS NOT NULL)
                  EXECUTE FUNCTION public.enforce_project_file_access();

                CREATE TRIGGER trg_files_project_update
                  BEFORE UPDATE ON public.files
                  FOR EACH ROW
                  WHEN (OLD.project_id IS NOT NULL OR NEW.project_id IS NOT NULL)
                  EXECUTE FUNCTION public.enforce_project_file_access();

                CREATE TRIGGER trg_files_project_delete
                  BEFORE DELETE ON public.files
                  FOR EACH ROW
                  WHEN (OLD.project_id IS NOT NULL)
                  EXECUTE FUNCTION public.enforce_project_file_access();

                CREATE FUNCTION public.enforce_pending_file_deletion_access()
                RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog, public, pg_temp
                AS $function$
                BEGIN
                  IF current_user <> 'drover_system' AND NOT EXISTS (
                    SELECT 1
                    FROM public.memberships AS member
                    WHERE member.space_id = sys.current_space()
                      AND member.user_id = sys.current_user_id()
                      AND member.role IN ('owner', 'admin')) THEN
                    RAISE EXCEPTION 'Only space owners and admins may change pending file deletions.'
                      USING ERRCODE = 'insufficient_privilege';
                  END IF;

                  IF TG_OP = 'INSERT' THEN
                    IF NEW.storage_key IS DISTINCT FROM (replace(NEW.space_id::text, '-', '') || '/' || replace(NEW.id::text, '-', '')) THEN
                      RAISE EXCEPTION 'Pending file deletion key must match the file.'
                        USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                  END IF;

                  IF TG_OP = 'UPDATE' THEN
                    IF NEW.id IS DISTINCT FROM OLD.id
                      OR NEW.space_id IS DISTINCT FROM OLD.space_id
                      OR NEW.project_id IS DISTINCT FROM OLD.project_id
                      OR NEW.storage_key IS DISTINCT FROM OLD.storage_key
                      OR NEW.size IS DISTINCT FROM OLD.size
                      OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
                      RAISE EXCEPTION 'Pending file deletion details cannot be changed.'
                        USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                  END IF;

                  RETURN OLD;
                END;
                $function$;

                REVOKE ALL ON FUNCTION public.enforce_pending_file_deletion_access() FROM PUBLIC;

                CREATE TRIGGER trg_pending_file_deletions_access
                  BEFORE INSERT OR UPDATE OR DELETE ON public.pending_file_deletions
                  FOR EACH ROW
                  EXECUTE FUNCTION public.enforce_pending_file_deletion_access();

                CREATE FUNCTION public.enforce_file_byte_reservation_access()
                RETURNS trigger
                LANGUAGE plpgsql
                SET search_path = pg_catalog, public, pg_temp
                AS $function$
                BEGIN
                  IF current_user <> 'drover_system' AND NOT EXISTS (
                    SELECT 1
                    FROM public.memberships AS member
                    WHERE member.space_id = sys.current_space()
                      AND member.user_id = sys.current_user_id()
                      AND member.role IN ('owner', 'admin')) THEN
                    RAISE EXCEPTION 'Only space owners and admins may change file byte reservations.'
                      USING ERRCODE = 'insufficient_privilege';
                  END IF;

                  IF TG_OP = 'INSERT' THEN
                    IF EXISTS (SELECT 1 FROM public.files AS f WHERE f.id = NEW.id)
                      OR EXISTS (SELECT 1 FROM public.pending_file_deletions AS d WHERE d.id = NEW.id) THEN
                      RAISE EXCEPTION 'File byte reservation id cannot match a stored or deleted file.'
                        USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                  END IF;

                  IF TG_OP = 'UPDATE' THEN
                    IF NEW.id IS DISTINCT FROM OLD.id
                      OR NEW.space_id IS DISTINCT FROM OLD.space_id THEN
                      RAISE EXCEPTION 'File byte reservation identity cannot change.'
                        USING ERRCODE = 'insufficient_privilege';
                    END IF;
                    RETURN NEW;
                  END IF;

                  RETURN OLD;
                END;
                $function$;

                REVOKE ALL ON FUNCTION public.enforce_file_byte_reservation_access() FROM PUBLIC;

                CREATE TRIGGER trg_file_byte_reservations_access
                  BEFORE INSERT OR UPDATE OR DELETE ON public.file_byte_reservations
                  FOR EACH ROW
                  EXECUTE FUNCTION public.enforce_file_byte_reservation_access();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER trg_file_byte_reservations_access ON public.file_byte_reservations;");
            migrationBuilder.Sql("DROP FUNCTION public.enforce_file_byte_reservation_access();");
            migrationBuilder.Sql("DROP TRIGGER trg_pending_file_deletions_access ON public.pending_file_deletions;");
            migrationBuilder.Sql("DROP FUNCTION public.enforce_pending_file_deletion_access();");
            migrationBuilder.Sql("DROP TRIGGER trg_files_project_delete ON public.files;");
            migrationBuilder.Sql("DROP TRIGGER trg_files_project_update ON public.files;");
            migrationBuilder.Sql("DROP TRIGGER trg_files_project_insert ON public.files;");
            migrationBuilder.Sql("DROP FUNCTION public.enforce_project_file_access();");

            migrationBuilder.DropTable(
                name: "file_byte_reservations");

            migrationBuilder.DropTable(
                name: "pending_file_deletions");
        }
    }
}

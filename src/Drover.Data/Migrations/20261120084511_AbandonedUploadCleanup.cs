#nullable disable

namespace Drover.Data.Migrations;

/// <inheritdoc />
public partial class AbandonedUploadCleanup : Migration
{
    #region Overrides

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pending_upload_deletions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                space_id = table.Column<Guid>(type: "uuid", nullable: false),
                audience_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                cleanup_claim_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_pending_upload_deletions", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_pending_upload_deletions_cleanup_claim_until_id",
            table: "pending_upload_deletions",
            columns: ["cleanup_claim_until", "id"]);

        migrationBuilder.IsolateBySpace("pending_upload_deletions", audience: true);
        migrationBuilder.Sql("REVOKE INSERT, UPDATE ON pending_upload_deletions FROM drover_app;");
        migrationBuilder.Sql("GRANT UPDATE (id, cleanup_claim_until) ON pending_upload_deletions TO drover_app;");

        // The trigger writes the deletion receipt. The discovery functions read only the columns used to find
        // eligible uploads and claim due receipts. No file names, storage keys, or content leave these functions.
        migrationBuilder.Sql("GRANT INSERT ON pending_upload_deletions TO drover_system;");
        migrationBuilder.Sql(
            """
            GRANT SELECT (id, space_id, audience_user_id, thread_id, project_id, message_id,
              created_by_user_id, created_by_bot_id, created_at) ON files TO drover_system;
            """);
        migrationBuilder.Sql(
            """
            GRANT SELECT (id, space_id, audience_user_id, cleanup_claim_until, created_at)
              ON pending_upload_deletions TO drover_system;
            """);
        migrationBuilder.Sql("GRANT UPDATE (cleanup_claim_until) ON pending_upload_deletions TO drover_system;");

        // This function can be invoked only as a delete trigger on the real files table. That relation guard
        // prevents an app session from binding it to a temporary table and forging a deletion receipt.
        migrationBuilder.CreateSystemFunction(
            "queue_upload_deletion",
            [],
            "trigger",
            """
            BEGIN
              IF TG_RELID <> 'public.files'::regclass
                OR TG_TABLE_SCHEMA <> 'public'
                OR TG_TABLE_NAME <> 'files'
                OR TG_OP <> 'DELETE' THEN
                RAISE EXCEPTION 'Upload deletion receipts come only from deleted stored files.'
                  USING ERRCODE = 'insufficient_privilege';
              END IF;

              INSERT INTO public.pending_upload_deletions (id, space_id, audience_user_id)
              VALUES (OLD.id, OLD.space_id, OLD.audience_user_id);

              INSERT INTO sys.audit (function_name, caller, row_count)
              VALUES ('queue_upload_deletion', session_user, 1);
              RETURN OLD;
            END;
            """);

        migrationBuilder.CreateSystemFunction(
            "expired_uploads",
            [("max_rows", "integer")],
            "TABLE (space_id uuid, audience_user_id uuid, file_id uuid, thread_id uuid)",
            """
            #variable_conflict use_column
            BEGIN
              RETURN QUERY
              WITH found AS (
                SELECT f.space_id AS found_space,
                       f.audience_user_id AS found_audience,
                       f.id AS found_file,
                       f.thread_id AS found_thread
                FROM public.files f
                WHERE f.project_id IS NULL
                  AND f.message_id IS NULL
                  AND f.thread_id IS NOT NULL
                  AND f.created_by_user_id IS NOT NULL
                  AND f.created_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid
                  AND f.created_by_bot_id IS NULL
                  AND f.created_at < now() - interval '1 day'
                ORDER BY f.created_at, f.id
                LIMIT least(greatest(max_rows, 0), 50)),
              logged AS (
                INSERT INTO sys.audit (function_name, caller, row_count)
                SELECT 'expired_uploads', session_user, count(*)::integer FROM found)
              SELECT found_space, found_audience, found_file, found_thread FROM found;
            END;
            """);

        migrationBuilder.CreateSystemFunction(
            "claim_pending_upload_deletions",
            [("max_rows", "integer")],
            "TABLE (space_id uuid, audience_user_id uuid, file_id uuid)",
            """
            #variable_conflict use_column
            BEGIN
              RETURN QUERY
              WITH picked AS (
                SELECT d.id
                FROM public.pending_upload_deletions d
                WHERE d.cleanup_claim_until IS NULL OR d.cleanup_claim_until <= now()
                ORDER BY d.created_at, d.id
                LIMIT least(greatest(max_rows, 0), 50)
                FOR UPDATE OF d SKIP LOCKED),
              claimed AS (
                UPDATE public.pending_upload_deletions
                SET cleanup_claim_until = now() + interval '5 minutes'
                FROM picked
                WHERE pending_upload_deletions.id = picked.id
                RETURNING pending_upload_deletions.space_id AS claimed_space,
                          pending_upload_deletions.audience_user_id AS claimed_audience,
                          pending_upload_deletions.id AS claimed_file),
              logged AS (
                INSERT INTO sys.audit (function_name, caller, row_count)
                SELECT 'claim_pending_upload_deletions', session_user, count(*)::integer FROM claimed)
              SELECT claimed_space, claimed_audience, claimed_file FROM claimed;
            END;
            """);

        migrationBuilder.Sql(
            """
            CREATE TRIGGER trg_files_queue_upload_deletion
              AFTER DELETE ON public.files
              FOR EACH ROW
              WHEN (OLD.project_id IS NULL
                AND OLD.message_id IS NULL
                AND OLD.thread_id IS NOT NULL
                AND OLD.created_by_user_id IS NOT NULL
                AND OLD.created_by_user_id <> '00000000-0000-0000-0000-000000000000'::uuid
                AND OLD.created_by_bot_id IS NULL)
              EXECUTE FUNCTION sys.queue_upload_deletion();

            CREATE FUNCTION public.enforce_pending_upload_deletion_access()
            RETURNS trigger
            LANGUAGE plpgsql
            SET search_path = pg_catalog, public, pg_temp
            AS $function$
            BEGIN
              IF TG_OP = 'INSERT' THEN
                IF current_user <> 'drover_system' THEN
                  RAISE EXCEPTION 'Only a stored file deletion may add a pending upload deletion.'
                    USING ERRCODE = 'insufficient_privilege';
                END IF;
                RETURN NEW;
              END IF;

              IF TG_OP = 'UPDATE' THEN
                IF NEW.id IS DISTINCT FROM OLD.id
                  OR NEW.space_id IS DISTINCT FROM OLD.space_id
                  OR NEW.audience_user_id IS DISTINCT FROM OLD.audience_user_id
                  OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
                  RAISE EXCEPTION 'Pending upload deletion identity cannot change.'
                    USING ERRCODE = 'insufficient_privilege';
                END IF;
                RETURN NEW;
              END IF;

              RETURN OLD;
            END;
            $function$;

            REVOKE ALL ON FUNCTION public.enforce_pending_upload_deletion_access() FROM PUBLIC;

            CREATE TRIGGER trg_pending_upload_deletions_access
              BEFORE INSERT OR UPDATE OR DELETE ON public.pending_upload_deletions
              FOR EACH ROW
              EXECUTE FUNCTION public.enforce_pending_upload_deletion_access();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER trg_pending_upload_deletions_access ON public.pending_upload_deletions;");
        migrationBuilder.Sql("DROP FUNCTION public.enforce_pending_upload_deletion_access();");
        migrationBuilder.Sql("DROP TRIGGER trg_files_queue_upload_deletion ON public.files;");
        migrationBuilder.DropSystemFunction("claim_pending_upload_deletions", "integer");
        migrationBuilder.DropSystemFunction("expired_uploads", "integer");
        migrationBuilder.DropSystemFunction("queue_upload_deletion", string.Empty);
        migrationBuilder.Sql("REVOKE UPDATE (cleanup_claim_until) ON pending_upload_deletions FROM drover_system;");
        migrationBuilder.Sql(
            """
            REVOKE SELECT (id, space_id, audience_user_id, cleanup_claim_until, created_at)
              ON pending_upload_deletions FROM drover_system;
            """);
        migrationBuilder.Sql(
            """
            REVOKE SELECT (id, space_id, audience_user_id, thread_id, project_id, message_id,
              created_by_user_id, created_by_bot_id, created_at) ON files FROM drover_system;
            """);
        migrationBuilder.Sql("REVOKE INSERT ON pending_upload_deletions FROM drover_system;");

        migrationBuilder.DropTable(
            name: "pending_upload_deletions");
    }

    #endregion
}

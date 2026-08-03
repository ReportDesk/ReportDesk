# Follow-up attachment migration and rollback runbook

## Scope and safety boundary

Historical `ATTACHMENT.FILE_PATH` values beginning with `/uploads/followups/` remain readable through the application's static-file middleware during the transition. New hardened follow-up uploads store a private `r2://` reference and must be delivered only through the authenticated attachment-delivery action; do not make an R2 bucket public to support this migration.

No migration runs automatically. `AttachmentDelivery:EnableMigrationReconciliation` and the independent `AttachmentDelivery:EnableMigrationExecution` switch both default to `false`. With reconciliation enabled, the Admin-only `GET /api/attachments/migration/reconcile` endpoint defaults to a read-only local dry run. Execution additionally requires `dryRun=false`, the execution switch, checkpoint and manifest paths outside `wwwroot`, and `AttachmentDelivery:MigrationLocalObjectStorePath` pointing to a separate local object-store root. The application registers a disabled adapter by default and fails startup if execution is enabled without that root; the normal live R2 upload service is never used by this workflow.

## Prerequisites

1. Deploy the private attachment-delivery action and verify that it authorizes the current session before obtaining the R2 object. It must map the attachment ID to its stored `r2://` key server-side and never send the key in a browser URL.
2. Keep `AttachmentDelivery:EnableLegacyFollowUpReads=true`; retain the `/api/management/followup/save` route and multipart field names during the transition.
3. Create a database backup and a read-only inventory of every legacy attachment ID, path, size, and checksum. Store the inventory and the migration manifest in access-controlled durable storage.
4. Configure a unique checkpoint file and audit-manifest destination for the run. Do not place either under `wwwroot`.

## Opt-in migration procedure

1. Set `AttachmentDelivery:EnableMigrationReconciliation=true` in local/deployment-only configuration. Keep `EnableMigrationExecution=false` while reviewing inventory. Do not add secrets to `appsettings.json`.
2. Call `GET /api/attachments/migration/reconcile?afterAttachmentId=0&batchSize=100&dryRun=true` as an Admin. Persist the JSON audit batch, then continue with its `nextCursor` until `hasMore=false`. Each record contains the stable attachment ID, legacy path, local-file status, size, SHA-256 checksum, and review outcome; rerunning from the same cursor is deterministic.
3. Review the dry-run manifest: reject traversal paths, missing files, duplicate checksums, and any record without a stable attachment ID. A missing source is recorded as `not-found`; it is not deleted or rewritten.
4. Configure unique absolute `MigrationCheckpointPath` and `MigrationAuditManifestPath` values outside `wwwroot`, plus an absolute `MigrationLocalObjectStorePath` outside `wwwroot`, and only then set `EnableMigrationExecution=true`. Re-run with `dryRun=false` and a small `batchSize`. For each record the workflow appends a prepared audit record, copies and checksum-verifies the object, updates only that row's `FILE_PATH` to `r2://…`, appends the completed audit record, and atomically advances the checkpoint. Missing/rejected sources are audited without rewriting them. A checksum failure leaves the database and checkpoint unchanged. The source file is never deleted. A rerun loads the durable checkpoint and skips completed rows, making interruption resumable and idempotent.
5. Monitor the structured `Legacy follow-up attachment path resolved` warning. Its count is the transition metric; a private-delivery error rate above the release threshold is a rollback trigger.

## Rollback

1. Stop the migration tool; do not delete either source local files or copied R2 objects.
2. Set `AttachmentDelivery:EnableMigrationExecution=false` and `AttachmentDelivery:EnableMigrationReconciliation=false`, clear the migration-local object-store configuration, and leave `AttachmentDelivery:EnableLegacyFollowUpReads=true`.
3. Use the immutable audit manifest to restore only rows changed by this run from their recorded source `/uploads/followups/...` paths. Re-run the inventory and checksum checks.
4. Leave copied private objects in quarantine until the incident is resolved; cleanup requires a separate, owner-approved retention task.
5. Confirm a historical local attachment returns a normal response, a missing legacy file returns 404 without a filesystem path, and a private attachment remains unavailable to an unauthenticated request.

## Deprecation and removal gate

The management follow-up save route remains supported for the transition. Its owner must emit its normal API deprecation response/header during the announced window. Do not remove the route, disable legacy reads, or delete local files until all of the following are recorded: legacy-use metric is zero for the agreed window, migration audit is complete, private delivery is monitored, rollback rehearsal succeeds, and the deployment owner explicitly approves removal.

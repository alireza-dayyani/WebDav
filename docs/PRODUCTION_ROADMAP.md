# Production roadmap for Inkstone

This is a development guide, not a claim of production capacity. The target is
3,000–4,000 registered accounts; simultaneous editors and document sizes must be
measured separately. Preserve the independent Vue frontend, .NET API and Rust engine.

## Current constraints

- SQLite contains Identity, document models, full revisions, artifacts and job inputs.
- SQLite FTS5 supplies search, including Persian/Arabic normalization.
- One API process owns a data directory using `instance.lock`. Do not remove this
  lock to enable replicas: startup recovery and worker ownership assume one process.
- Conversion launches the native CLI with a timeout and temporary files.
- Autosaves submit the entire model; revisions and indexing can amplify writes.
- Background jobs are persisted but workers, cancellation and recovery are local.
- Cookies depend on persistent local Data Protection keys.
- Backup tooling currently requires stopping the app for a consistent data/key snapshot.

## Application work, in delivery order

1. Protect interactive validation capacity from exports. Add per-account API limits
   and privacy-preserving recipient lookup. Instrument saturation and latency.
2. Add email confirmation, password reset, session revocation and administrator MFA.
   Use expiring, single-use Identity tokens, generic recovery responses, delivery
   retries and tests for expired/replayed tokens. Do not simulate email delivery in production.
3. Enforce transactional storage and processing quotas. Account for current models,
   history, Trash, snapshots, artifacts, and queued inputs. Concurrent requests must
   reserve capacity atomically; release reservations on failure/cancellation.
4. Improve save retries, conflict recovery and session-expiry handling. Preserve
   unsaved work; never acknowledge a save before durable storage succeeds. Any local
   draft recovery requires an explicit privacy policy and account isolation.
5. Introduce workspace membership and invitation-based sharing if organizations are
   the intended customers. Carry tenant scope through every query, job and index.
6. Measure redundant autosaves and history growth. Avoid identical writes; design
   checkpoint/coalescing rules without altering review evidence or version guards.

## Infrastructure work for future developers

| Area | Decision and implementation work | Acceptance evidence |
| --- | --- | --- |
| PostgreSQL, SQL Server or MySQL | Choose one supported production provider based on operating expertise, hosting and measured workload. PostgreSQL is the default candidate, not an implemented dependency. SQL Server and MySQL are alternatives, not additional required databases. Inventory SQLite-specific SQL, types, collations, concurrency behavior, Identity and FTS before selecting. | Provider-backed integration tests prove ownership, stale-write rejection, idempotency, retention and transaction rollback. |
| Database migration | Replace startup schema mutation with reviewed, versioned migrations for the selected provider. Rehearse copying IDs, timestamps, grants (including revoked versions), reviews, revisions, receipts and jobs. Freeze writes or implement a verified cutover protocol. | Row counts, content hashes, permissions and restore checks match. Rollback procedure is rehearsed before cutover. |
| Private object storage | Introduce a storage interface and immutable blob keys for models, assets and exports. Keep metadata and revision references in SQL. Coordinate blob writes and DB commits with staging/outbox and orphan cleanup; never expose public buckets. | Failed writes cannot create acknowledged missing revisions. Download authorization, expiration, deletion and backup restore pass. |
| Search index | Start with the chosen SQL provider's search if sufficient. Evaluate a dedicated index only after measuring relevance and latency. Use a durable outbox, document version checks, replayable indexing and reconciliation. Preserve Persian normalization. | Revocation/deletion cannot leak documents through titles, snippets, counts or stale hits. Backfill and index rebuild work without losing writes. |
| Durable workers | Separate API and Rust workers. Use job leases, heartbeat, bounded retry, idempotency and durable cross-process cancellation. Add fair per-account scheduling and limits on queued input bytes. | Kill a worker mid-job, restart the API and run two workers: one logical result, recoverable ownership and no cross-account results. |
| Docker and runtime isolation | Develop and validate deployment recipes separately. Build platform-matched CLI/editor assets in reproducible stages, run with least privilege, configure resource limits, private temporary directories, health checks and graceful shutdown. Restrict converter network/filesystem access independently of packaging. | Build/run checks, termination during conversion, temp-file cleanup and restart recovery pass. |
| Multiple API instances | Share protected Data Protection keys with the same application name; externalize persistent state and distributed rate-limit/worker coordination. Configure trusted proxy headers explicitly. | Login survives switching nodes, revocation works across nodes, and rolling restart does not invalidate healthy jobs. |
| Observability | Export HTTP/save latency, converter queue wait, active slots, timeouts, queue age, DB contention, storage and backup status. Use request/job IDs. Exclude document bodies, tokens and credentials from telemetry. | Alerts are exercised using injected failures; metrics have no unbounded user/document-ID labels. |
| Backup and recovery | Schedule encrypted off-machine backups of SQL, blobs and required keys. Set explicit RPO/RTO and retention. Provider-native online backups replace the current stop-and-copy workflow when needed. | Restore a clean environment and verify documents, historical reviews and permissions within the agreed recovery target. |
| Static assets and delivery | Cache fingerprinted WASM/JS/font assets with compression and coordinated releases. Keep private API responses out of shared caches. | A browser with cached old assets can upgrade without loading mismatched editor/bridge bundles. |
| Release operations | Add staging, provider migration rehearsals, dependency scanning, secret management and rollback procedures. | Deploy/rollback drills preserve data and backward-compatible API behavior. |

Redis is optional: introduce it for a demonstrated distributed coordination/cache
need. Kubernetes and separate services for every feature are not prerequisites.

## Capacity validation

Create repeatable tests for 50, 150 and 300 active editors before raising the target.
Mix editing, library search, previews, imports and bulk exports; include Persian,
tables, images and realistic revision counts. Measure p50/p95/p99 latency, save
success, converter rejections, memory, DB waits and storage growth. Include burst,
soak, restart and permission-revocation tests. Browser tests are required alongside
HTTP tests because WASM load time and client memory are separate constraints.

An illustrative 300 changing documents saving every 10 seconds implies roughly
30 saves/sec. This is a workload model, not a benchmark or server-sizing promise.
Define latency/error and recovery targets before declaring the beta ready.

## First application batch

The initial implementation reserves independent converter pools for native
preflight and other conversion work, adds process-local API request budgets, and
changes recipient discovery to self lookup or an exact email address. These do not
provide multi-instance coordination, verified email ownership, tenant membership,
storage quotas or certified capacity. See the backend README for settings.

For every subsequent batch: update this guide's status, build the affected projects,
run relevant integration/browser checks, and report the proof boundary. Keep
databases, customer files, secrets and generated bundles out of source control.

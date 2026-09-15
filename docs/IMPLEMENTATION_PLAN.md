# .NET Job Hunter - Implementation Plan

> This plan implements the approved requirements in [PRD.md](PRD.md). Any
> material change to scope, security, deployment, or source behavior must update
> the PRD before or in the same change.

## 1. Delivery principles

1. Build a usable DOU + rules + Telegram path before optional AI or LinkedIn.
2. Keep the .NET Worker as the sole owner and writer of SQLite.
3. Use contract boundaries for unstable/external systems: DOU, JobSpy, Copilot,
   Telegram, filesystem/secrets, and time.
4. Persist intent before external side effects.
5. Treat source data and AI input as untrusted; no agent tools for AI scoring.
6. Use fixtures and mocked transports, not job boards or Telegram, in normal CI.
7. Prefer explicit degraded state and diagnostics over hidden retry/fallback.

## 2. Repository layout

```text
src/
  JobHunter.Domain/
  JobHunter.Application/
  JobHunter.Infrastructure/
  JobHunter.Worker/
  JobHunter.JobSources.Dou/
  JobHunter.JobSources.JobSpy/
  JobHunter.AI.Abstractions/
  JobHunter.AI.Copilot/
  JobHunter.Notifications.Telegram/
services/
  jobspy-api/
tests/
  JobHunter.Domain.Tests/
  JobHunter.Application.Tests/
  JobHunter.Infrastructure.Tests/
  JobHunter.ContractTests/
  JobHunter.IntegrationTests/
  Fixtures/
deploy/
  compose/
  secrets/
  examples/
```

## 3. Work packages

### WP-00: Feasibility spikes

**Goal:** retire integration uncertainty before committing production architecture.

| Spike | Work | Exit criterion |
|---|---|---|
| Copilot in container | Create disposable .NET 10 Linux container using `GitHub.Copilot.SDK`; authenticate using a supported noninteractive/deployable method; start a constrained session and validate structured tool output | Works after container restart without an OpenAI API key, or a documented blocker produces a rules-only MVP decision |
| Copilot tool lockdown | Verify permission handler denies filesystem, shell, browser, network, MCP, and arbitrary custom tool access; expose only `submit_job_analysis` | Adversarial prompt cannot cause a tool action |
| JobSpy boundary | Pin package/image; wrap one request into FastAPI JSON response; simulate normal, partial, 403/429, timeout, malformed result | .NET can distinguish `Succeeded`, `Partial`, `Blocked`, `Failed` |
| DOU parser | Capture permitted, minimized RSS fixtures, parse dates/link/GUID/escaped HTML | No live source required for parser tests |
| Compose persistence | Start/restart service against named volume and prove SQLite/WAL state survives | No data loss after recreation |

**Decision gate:** If Copilot cannot authenticate safely in the target deployment,
ship MVP with `IJobAnalyzer` and the adapter disabled. Do not delay DOU/rules/
Telegram work or introduce an unsupported credential workaround.

### WP-01: Solution and operational foundation

**Deliverables**

- .NET 10 solution and project references.
- `Directory.Build.props` for nullable reference types, warnings policy,
  deterministic builds, analyzers, and central package management if adopted.
- Configuration binding and `ValidateOnStart`.
- Absolute application data paths; no dependence on working directory.
- `IDateTimeProvider`/`TimeProvider`, cancellation propagation, and host
  shutdown configuration.
- Singleton process/instance guard before migrations and scheduling.
- SQLite context factory, migrations, foreign keys, WAL initialization, bounded
  busy timeout, and startup migration protocol.
- Dockerfiles, local `.env.example`, Compose file, named data volume, secret
  mounts, non-root containers, and health checks.
- Structured logging, privacy redaction policy, basic OpenTelemetry activity/
  metrics wiring, and loopback-only health/readiness endpoint if needed.

**Validation**

- `dotnet build` and baseline tests pass.
- Starting a second worker exits before migration or scan.
- Migrations can apply to an empty mounted volume.
- Readiness is false until database/migration/lock setup succeeds.
- Container runs as non-root and secrets are not present in image layers.

### WP-02: Domain model and persistence

**Deliverables**

- Value objects/enums for source name, native source ID, canonical URL, job key,
  score, source run status, job lifecycle, and outbox status.
- EF entities/mappings for every PRD data-model entity.
- Unique indexes:
  - `(Source, SourceJobId)` when ID exists;
  - `(Source, CanonicalUrl)` when no native ID exists;
  - `(DestinationId, JobId, NotificationVersion)` for outbox.
- Optimistic/concurrency strategy and short transaction helpers.
- Source run lease/heartbeat and expiration/recovery behavior.
- Consistent backup command and restore/integrity-check command.

**Tests**

- Same source ID updates one job.
- URL tracking parameter variations produce one canonical URL.
- Changed content creates exactly one revision.
- Fuzzy cross-source match remains two jobs plus a possible-duplicate relation.
- Restart following abandoned lease safely resumes work.
- Backup restores to a separate database and passes SQLite integrity check.

### WP-03: Profile and deterministic evaluation

**Deliverables**

- YAML and JSON schema for `CandidateProfile`.
- Configurable profile file discovery/mount path.
- Markdown supplemental CV loader with size cap and contact-data redaction.
- Validation errors with field path and remediation hint.
- Hard-filter engine with explicit rule/result/reason/evidence ID.
- Versioned deterministic rubric and configurable thresholds.
- `RuleEvaluation` persistence and human-readable, privacy-safe explanation.

**Tests**

- Invalid YAML/JSON is rejected at startup or profile reload with useful error.
- Rules do not infer data that is absent.
- Required skills, exclusions, remote policy, location, salary, and score
  calculations are deterministic.
- Profile/rubric change produces a new versioned evaluation rather than mutating
  historical result.

### WP-04: DOU adapter

**Deliverables**

- `IJobSource`, `JobSourceResult`, source cursor, typed HTTP client and
  per-source resilience policy.
- RSS parser using safe XML handling, bounded content and explicit date parsing.
- URL canonicalizer shared by all sources.
- HTML-to-plain-text extraction and safe stored HTML policy.
- Optional DOU detail-page enrichment adapter with strict source rate limit.
- Conditional HTTP support only when the upstream provides valid validators.
- Source health, retry-after/backoff, parser-version and payload/content hash.

**Fixture inventory**

- Standard .NET category RSS.
- Keyword RSS.
- Ukrainian escaped description.
- Missing/invalid date.
- Tracking URL/GUID variation.
- Duplicate item.
- Malformed XML.
- Changed/unsupported content type.
- 304, 403, 429, 5xx, and timeout transport envelopes.

**Acceptance**

- A fixture import persists jobs/observations and safely re-runs idempotently.
- A malformed source response does not mutate jobs or crash other sources.
- Source failure creates observable degraded state and bounded retry.

### WP-05: JobSpy service and .NET adapter

**Python service deliverables**

- Minimal FastAPI app, Pydantic request/response models, `/health`, `/version`,
  and `/v1/search`.
- Exact lockfile/package pin; image built from a minimal supported Python base.
- Allow-list source selection and bounded request/result sizes.
- Source-specific timeouts/concurrency limit; LinkedIn default `1`.
- Clear status/error envelope, including partial result and retry/backoff hint.
- No data volume mount, no Telegram token, no profile/CV, and no SQLite access.

**.NET deliverables**

- Typed internal HTTP client, contract validation, cancellation, and readiness
  awareness.
- Explicit opt-in/acknowledgement configuration validation.
- 403/429/sign-in/challenge -> `Blocked` with durable source state.
- Dashboard-free but discoverable status through logs/health/`doctor`.

**Acceptance**

- Default Compose configuration has JobSpy source disabled.
- A 429 fixture blocks only this source; DOU scan still completes.
- Unknown extra JSON fields are tolerated; missing required contract fields fail
  safely and mark source degraded.

### WP-06: Scoring pipeline orchestration

**Deliverables**

- `ScanOrchestrator` checks due source subscriptions using persisted times.
- Bounded source fetch concurrency and serialized/short persistence writes.
- Normalization -> deduplication -> hard filter -> rules score -> optional AI ->
  notification decision pipeline.
- Idempotent work item identifiers and durable step result/status.
- No repeat notification for an unchanged previously-sent qualifying job.
- Controlled re-evaluation policy for profile/rubric/job revision changes.

**Acceptance**

- Multiple scheduler ticks cannot process one subscription simultaneously.
- A job stored before crash resumes without a duplicate record.
- AI-disabled path reaches notification decision normally.
- Partial source run never marks absent jobs removed.

### WP-07: AI abstraction and Copilot adapter

**Deliverables**

- `IJobAnalyzer`, request/result DTOs, capability declaration, and provider
  selector.
- One versioned JSON schema/tool contract for criterion scores, confidence,
  evidence references, mismatch reasons, insufficient evidence, and provider
  metadata.
- Fixed scoring instructions and source-data delimiters.
- CV text redaction/minimization and bounded input/output tokens/characters.
- Copilot lifecycle wrapper: start client, fresh constrained session per
  analysis, strict permission denial, timeout, cancellation, result extraction,
  local schema/evidence validation, and safe disposal.
- AI queue/backpressure, small retry budget only for truly transient failures,
  failure classification, telemetry, and optional session credit cap.
- `NullJobAnalyzer` / disabled provider behavior.

**Critical validation**

- Copilot raw text does not become a score without local structured validation.
- Evidence ID must refer to supplied profile/job fragment; fabricated IDs fail.
- Injection fixtures such as "ignore prior rules", tool requests, secret
  extraction requests, malicious URLs, and hidden-instruction-like content
  cannot alter tool permissions or output schema.
- AI failure is recorded but does not fail source run or persistence.

**Future adapters**

- `OllamaJobAnalyzer`: local `/api/chat`, schema in `format`, low temperature,
  independent validation.
- `OpenAiJobAnalyzer`: Responses API structured output; opt-in cloud settings,
  spend and data retention controls.

### WP-08: Telegram notifications

**Deliverables**

- Telegram bot setup workflow that validates destination ownership/chat ID.
- `INotificationChannel` and `TelegramNotificationChannel`.
- Pure renderer with HTML escaping, trusted URL validation, length checking, and
  no sensitive content.
- Durable outbox dispatcher with lease/retry classification.
- Per-destination rate limiter, 429 `retry_after` handling, transient backoff,
  unknown-outcome policy, permanent destination failure behavior.
- Delivery attempt audit fields including safe external message ID only after
  confirmed success.

**Tests**

- Every dynamic field is escaped.
- Rendered message is <= 4,096 characters.
- 429 waits/requeues correctly.
- 403 disables destination.
- Timeout after request causes `Unknown`, not automatic duplicate send.
- Crash before/after intent/send/confirmation produces valid recovery state.

### WP-09: Operator experience and release hardening

**Deliverables**

- `doctor`, `run-once`, migration, backup, restore, and setup documentation.
- Example profile and sanitized source fixtures.
- Compose onboarding, secret provisioning, first Telegram message, source
  enabling/disabling, and upgrade/rollback docs.
- Retention/cleanup scheduled job for permitted data.
- Privacy-safe metrics/log schema and alert/freshness guidance.
- CI: formatting, build, unit tests, contract tests, integration tests, image
  build, image vulnerability/dependency scan according to repository policy.
- Multi-architecture build if `linux/amd64` and `linux/arm64` are in scope.

## 4. Dependency strategy

Pin stable versions compatible with .NET 10; do not introduce prereleases merely
because they are newer.

| Area | Package/tool |
|---|---|
| Hosting/config/logging | Microsoft.Extensions.* |
| Persistence | Microsoft.EntityFrameworkCore.Sqlite, Design |
| HTTP resilience | Microsoft.Extensions.Http.Resilience |
| Copilot | GitHub.Copilot.SDK |
| Telegram | Telegram.Bot |
| DOU feed | XmlReader or System.ServiceModel.Syndication |
| HTML enrichment | AngleSharp |
| Observability | OpenTelemetry.Extensions.Hosting + selected instrumentation |
| Tests | xUnit + a fake HTTP handler/test server |
| Python | python-jobspy, fastapi, uvicorn, pydantic, pytest, httpx |

## 5. Testing matrix

| Layer | Required tests |
|---|---|
| Domain | URL canonicalization, fingerprints, lifecycle, score math, threshold decision |
| Application | Scan state, idempotency, source failures, AI optionality, outbox transitions |
| DOU contract | XML fixtures, encoding, dates, malformed responses, tracking URLs |
| JobSpy contract | Schema compatibility, partial/blocked/failure envelopes, timeout |
| Infrastructure | SQLite migrations, unique constraints, WAL/busy recovery, backup/restore |
| Copilot | Output validation, no-tools policy, timeout, injection corpus, invalid provider output |
| Telegram | Escaping, limits, 429, transient/permanent failures, unknown send outcome |
| Integration | Fixture -> normalize -> persist -> rules -> outbox -> mocked Telegram |
| Compose smoke | Start, health, volume persistence, dependency isolation, secrets permissions |

## 6. Operational commands

The final command names can vary, but capabilities must exist:

```text
job-hunter doctor
job-hunter migrate
job-hunter run
job-hunter run-once --source dou
job-hunter setup-telegram
job-hunter backup --output <safe-path>
job-hunter restore --input <safe-backup>
```

`run-once` is the primary local troubleshooting and test mode. It must not
silently use production secrets outside explicitly configured local environment.

## 7. Compose design

```text
services:
  worker:
    image: job-hunter-worker
    depends_on:
      jobspy:
        condition: service_healthy
    volumes:
      - job-hunter-data:/var/lib/job-hunter
      - ./profile.yaml:/app/config/profile.yaml:ro
    secrets:
      - telegram_bot_token
      - copilot_credential_optional

  jobspy:
    image: job-hunter-jobspy
    expose:
      - "8080"
    # No mounted application data or application secrets.

volumes:
  job-hunter-data:
```

`depends_on` health ordering is a convenience only. The worker must tolerate
JobSpy being unavailable and continue DOU work. The Python service must not
write SQLite; the worker must not depend on JobSpy for its own readiness.

## 8. Security review checklist

- [ ] All AI sessions have no filesystem, shell, browser, network, MCP, or
      arbitrary tool access.
- [ ] Source HTML/text is treated as untrusted and bounded.
- [ ] Copilot authentication method is supported for the selected deployment and
      survives a controlled restart.
- [ ] Docker secrets are not present in logs, image layers, env dumps, or DB.
- [ ] LinkedIn experimental opt-in warning is explicit; blocking has no bypass.
- [ ] DOU and JobSpy clients have finite timeout, concurrency, and retry budgets.
- [ ] SQLite exists only in a local named volume and is written only by .NET.
- [ ] Telegram HTML is escaped and URLs are allowed HTTPS URLs.
- [ ] Outbox does not claim exact-once delivery.
- [ ] Backup has access controls and restore has been tested.

## 9. Release checklist

- [ ] WP-00 through WP-09 acceptance criteria complete.
- [ ] PRD acceptance scenarios pass.
- [ ] All normal automated tests use fixtures/mocks, not production job boards.
- [ ] Dependency versions and licenses reviewed.
- [ ] Default source configuration enables DOU only.
- [ ] JobSpy/LinkedIn cannot run without both enablement and risk acknowledgement.
- [ ] AI is disabled by default and no OpenAI key is required.
- [ ] AI-disabled pipeline has an end-to-end passing test.
- [ ] Copilot adapter has passing container smoke test or is explicitly disabled
      with documented rules-only fallback.
- [ ] Telegram test destination is distinct from a personal production chat.
- [ ] Database survives container recreation; backup restore is verified.
- [ ] Logs/metrics privacy review complete.
- [ ] README onboarding follows a clean-machine rehearsal.

## 10. Explicit deferred decisions

These decisions must be made only when their relevant phase begins:

1. Exact Copilot authentication method in Docker after WP-00 evidence.
2. AI model selection, budget, maximum analysis size, and quality threshold.
3. Profile schema field names and default scoring values after real fixture review.
4. Job retention duration and encrypted backup location.
5. DOU detail-page enrichment necessity after RSS field-gap measurement.
6. Native Windows/macOS/Linux service packaging after Docker MVP stabilizes.
7. Telegram inbound commands/buttons after a persistent saved/application workflow
   exists.
8. Ollama model and hardware support after quality/latency evaluation.

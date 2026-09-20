# .NET Job Hunter - Product Requirements Document

| Field | Value |
|---|---|
| Product | .NET Job Hunter |
| Status | Approved for implementation |
| Version | 2.0 |
| Last updated | 2026-09-20 |
| Target user | One job seeker operating a local installation |
| Primary runtime | Native .NET Worker on one local machine |

## 1. Product Vision

Job Hunter is a local .NET service that discovers .NET vacancies, retains a
private durable history, evaluates semantic fit against a compact candidate
profile with GitHub Copilot, and sends accepted vacancies once to a private
Telegram chat.

Copilot analysis is mandatory for qualification and notification. Explicit hard
filters remain local and deterministic. A job is still normalized and persisted
when analysis fails after ingestion, but its notification is deferred until a
later eligible AI retry. `run` and `run-once` do not start a scan when the
configured Copilot model is unavailable.

## 2. Goals

- Discover DOU .NET vacancies every 10-15 minutes without manual work.
- Support experimental, opt-in LinkedIn discovery through an isolated JobSpy
  sidecar without Python dependencies in the .NET worker.
- Persist jobs, revisions, source runs, hard-filter audits, AI results, and
  notification attempts in local SQLite.
- Use a compact YAML/JSON profile with target titles, optional required skills,
  small explicit hard filters, semantic AI preferences, and an optional CV.
- Require validated Copilot `overallFit` analysis for every notification.
- Send one immediate Telegram message for each accepted job and never repeat a
  confirmed delivery for the same job and destination.
- Run the .NET worker natively on Windows, macOS, and Linux without Docker.

## 3. Success Criteria

| Metric | MVP target |
|---|---:|
| DOU discovery delay under normal upstream availability | <= 20 minutes |
| Duplicate rows for repeated source records | 0 |
| Duplicate confirmed Telegram sends for a notification key | 0 |
| AI unavailable before a scan | `run` and `run-once` stop before scanning |
| AI unavailable during evaluation | Job persists; notification is deferred |
| Parser tests | No live job-board dependency |
| Secrets in source control or logs | 0 |
| Telegram message length | <= 4,096 characters |

## 4. Non-Goals

- Automatic job applications, a public web UI, desktop UI, or multi-user SaaS.
- LinkedIn login, cookie reuse, browser automation, CAPTCHA solving, proxy
  rotation, or any access-control evasion.
- PDF/DOCX CV import; profile data is YAML/JSON plus optional Markdown CV.
- Telegram inbound commands, webhooks, or callback buttons.
- Exact-once Telegram delivery.
- Worker Docker/Compose packaging as a prerequisite for native operation.
- Vector search, embeddings, automated application tracking, Quartz, or
  Hangfire.

## 5. Primary Journey

1. The user configures a private Telegram chat and authenticates the Copilot CLI.
2. The user creates a v2 candidate profile and optional Markdown CV.
3. The worker validates the configured Copilot model before `run` or `run-once`.
4. Sources create normalized jobs, observations, and revisions in SQLite.
5. Hard filters reject explicit mismatches with auditable reason codes.
6. Copilot scores semantic fit from bounded, redacted profile and job evidence.
7. A validated `overallFit` score at or above `AI:MinimumFitScore` creates an
   outbox intent; lower scores are AI rejections.
8. Telegram delivers the intent once and records the outcome.

## 6. Functional Requirements

### FR-01: Lifecycle and Scheduling

- The application is a .NET 10 Generic Host/Worker Service.
- Supported startup paths are `dotnet run` and a published native executable.
- It exposes `run`, `run-once`, `doctor`, `show-profile`, `setup-telegram`,
  migration, backup, restore, and integrity-check commands.
- A persisted scheduler checks due subscriptions at least every minute and never
  overlaps scans for one subscription.
- Lease recovery, schedules, and backoff use UTC and survive restart.
- Graceful shutdown stops new work, honours a bounded timeout, persists state,
  and releases execution leases.

### FR-02: DOU Source

- DOU RSS is the primary discovery source, including the .NET category feed and
  configured keyword feeds.
- Poll no more frequently than source cache guidance; default to 10-15 minutes
  plus jitter for a 600-second cache interval.
- Parse RSS title, link, GUID, publication date, description, and metadata.
- Canonical identity URLs remove tracking parameters such as `utm_*` and `from`.
- Optional detail-page enrichment fetches only new/changed/incomplete jobs, is
  separately rate-limited, and is fixture-tested.
- Feed absence alone never marks a job closed.

### FR-03: Experimental JobSpy / LinkedIn Source

- JobSpy is an isolated Python/FastAPI sidecar with a versioned loopback API.
- It is disabled by default and requires explicit configuration and risk
  acknowledgement.
- It never receives SQLite paths, Telegram credentials, candidate profile/CV, or
  Copilot credentials.
- It reports `Succeeded`, `Partial`, `Blocked`, or `Failed`.
- A 403, 429, sign-in redirect, challenge, or parser incompatibility pauses only
  this subscription with a durable degraded state and actionable diagnostic.
- The product does not add evasion mechanisms.

### FR-04: Normalization, Retention, and Deduplication

- Every source record becomes a source-neutral `Job` aggregate.
- Persist source ID, URLs, GUID, parser/raw/content hashes, query, retrieval
  timestamp, normalized job fields, observations, and revisions.
- Enforce unique `(source, source_job_id)` when present, otherwise `(source,
  canonical_url)`.
- Retain cross-source similarity as an auditable relationship; never merge jobs
  automatically.
- Keep core jobs, revisions, profiles, and evaluations; clean completed
  operational records after a configurable 30-day default retention period.
- Scrub terminal outbox payloads while retaining their unique-key tombstones.

### FR-05: Candidate Profile

- Accept validated YAML or JSON and optional Markdown CV text.
- Schema version `2` has only `targetTitles`, optional `requiredSkills`, optional
  `hardFilters`, optional `aiPreferences`, and `supplementalCvPath`.
- Hard filters may contain locations, remote policy, excluded employers, and
  excluded keywords.
- AI preferences are bounded evidence, not instructions, and never bypass hard
  filters.
- Redact contact details and unrelated sensitive data before AI analysis.
- Validation reports field paths and remediation guidance before a scan.

### FR-06: Hard Filters and Qualification

- Hard filters produce explicit reason codes, including `ExcludedEmployer`,
  `ExcludedKeyword`, `MissingRequiredSkill`, `LocationMismatch`, and
  `RemotePolicyMismatch`.
- Filters do not infer unstated skills, location, or workplace mode.
- Hard-filter outcomes are versioned audit records; they do not calculate a fit
  score.
- Validated Copilot `overallFit` is the only qualification score after hard
  filters. `AI:MinimumFitScore` is application configuration, not profile data.
- A score below the threshold is an AI rejection. Insufficient or invalid AI
  output creates no notification intent.

### FR-07: Mandatory AI Analysis

- `IJobAnalyzer` remains provider-neutral and supports availability discovery,
  cancellation, deadlines, structured output, provider/model version, usage,
  warnings, and schema/policy version.
- GitHub Copilot SDK is the required initial implementation. Future Ollama and
  OpenAI adapters must use the same contract.
- `run` and `run-once` validate the configured model before scanning. An
  unavailable model stops the command; it never silently changes models.
- Every analysis uses a fresh restricted session with no filesystem, shell,
  browser, network, MCP, skill, or arbitrary custom tools. Only
  `submit_job_analysis` is available.
- Prompt templates and schemas are application-owned. Vacancy, CV, links, and
  profile text are untrusted evidence, not instructions.
- Output is locally validated, evidence references must be supplied IDs, and an
  invalid submission receives at most one corrective retry in a fresh session.
- Invalid, timed-out, unavailable, refused, over-budget, or low-confidence
  output does not fail ingestion. It is persisted and notification is deferred;
  transient statuses become eligible for a later attempt.

### FR-08: Telegram

- Send only to a configured, verified private chat that the user initiated.
- Use a durable outbox, HTML escaping, URL validation, a 4,096-character limit,
  per-destination rate limiting, bounded retries, and persisted `retry_after`
  cooldowns.
- Timeout outcomes are unknown, not successful; do not claim exactly-once
  external delivery.
- The MVP sends at most one notification per job and destination, including
  after a profile, policy, or job revision.

### FR-09: Operations and Observability

- Log structured, privacy-safe source, filter, AI, outbox, and delivery states.
- Never log CV bodies, full vacancy bodies, Telegram payloads, secrets, or
  private chat IDs.
- `doctor` validates the application-data directory, database, configured
  sources, mandatory Copilot availability, and enabled Telegram/JobSpy contracts.
- Backup, restore, and integrity-check commands use explicit absolute paths;
  restore never overwrites an existing database.

## 7. Data Model

| Entity | Core responsibility |
|---|---|
| `CandidateProfile` | Versioned targets, hard filters, semantic preferences, CV path |
| `SourceSubscription` / `SourceRun` | Schedule, cursor, lease, outcome, backoff, diagnostics |
| `Job` / `JobRevision` / `JobObservation` | Identity, normalized data, provenance, history |
| `JobPossibleDuplicate` | Reviewable cross-source relationship |
| `RuleEvaluation` | Versioned hard-filter audit outcome |
| `AiAnalysis` | Provider/model/schema/policy result and retained metadata |
| `NotificationOutbox` / `DeliveryAttempt` | Durable intent, lease, delivery state, retry audit |

## 8. Security and Privacy

- The .NET worker is the only SQLite writer. Store SQLite in an OS-appropriate
  local application-data directory, never a network share.
- Secrets come from platform-protected or access-controlled configuration and are
  never committed, written to SQLite, or logged.
- JobSpy receives only its narrow request contract and has no local data volume
  or application credentials.
- Contact and unrelated sensitive data are redacted before cloud AI calls.
- Prompt-injection defence combines evidence delimiting, no tools, fixed
  instructions, bounded input, local schema validation, evidence IDs, and
  adversarial fixtures.
- Do not score protected characteristics or proxies for them.
- AI evaluates personal job fit only; it is not an autonomous hiring or legal
  eligibility system.

## 9. Configuration Model

Safe defaults live in `appsettings.json`; secrets and environment-specific
settings are external. Nested environment keys use `__`.

```yaml
worker:
  schedulerTickSeconds: 60
sources:
  dou:
    enabled: true
  linkedinJobSpy:
    enabled: false
    experimentalAcknowledged: false
ai:
  provider: copilot
  analysisTimeoutSeconds: 60
  minimumConfidence: 0.65
  minimumFitScore: 75
telegram:
  enabled: true
```

## 10. Acceptance Scenarios

1. A first DOU observation is normalized, hard-filtered, analyzed, and creates
   exactly one notification intent only when the validated AI score qualifies.
2. Repeated source observations with tracking-URL variations retain one job and
   produce no duplicate notification.
3. A job revision or profile/policy revision creates a new evaluation identity
   without overwriting historical evidence.
4. A missing Copilot model stops `run` and `run-once` before a source scan.
5. An analysis timeout or invalid structured result after ingestion persists the
   result, creates no notification intent, and retries when eligible.
6. JobSpy blocked states never stop DOU processing or invoke evasion behaviour.
7. Telegram 429 persists destination cooldown; timeout remains `Unknown` and is
   not silently resent.

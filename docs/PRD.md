# .NET Job Hunter - Product Requirements Document

| Field | Value |
|---|---|
| Product | .NET Job Hunter |
| Status | Approved for implementation |
| Version | 1.1 |
| Last updated | 2026-09-15 |
| Target user | One job seeker operating a local installation |
| Primary runtime | Native .NET Worker on one local machine |

## 1. Product vision

Job Hunter is a local, always-on assistant that finds suitable .NET vacancies,
remembers what it has already seen, evaluates each new vacancy against the
user's explicit profile, and delivers only relevant opportunities to a private
Telegram chat.

The product must remain useful without an AI provider: deterministic filters and
scoring are a complete baseline. AI adds semantic matching and explanations; it
must never be a dependency for discovering, storing, or deduplicating vacancies.

## 2. Problem statement

Searching several job boards repeatedly creates three problems:

1. New vacancies are easy to miss between manual searches.
2. The same vacancy appears repeatedly, sometimes on several boards.
3. Reading every description is expensive; keyword matches alone do not reflect
   a candidate's actual stack, seniority, location, work-style, or role goals.

The system solves this by continuously collecting vacancies, retaining durable
history, applying transparent rules, optionally augmenting them with constrained
AI analysis, and notifying the user only once per qualifying vacancy.

## 3. Goals and measurable outcomes

### 3.1 Goals

- Discover .NET-relevant DOU vacancies every 10-15 minutes without manual work.
- Support experimental opt-in LinkedIn discovery through a separate JobSpy
  service without coupling Python dependencies into the .NET worker.
- Persist all discovered jobs, observations, scoring results, revisions, source
  failures, and notification attempts locally.
- Detect repeated source observations and avoid repeat Telegram messages.
- Let the user define their profile, preferred roles, locations, stack, salary
  floor, and exclusions in version-controlled structured data.
- Provide an optional AI evaluator behind `IJobAnalyzer`.
- Use GitHub Copilot SDK as the first evaluator, with future-ready boundaries for
  local Ollama and an OpenAI API adapter.
- Send one immediate Telegram message for each job above its applicable
  notification threshold.
- Run directly on Windows, macOS, and Linux without Docker.
- Preserve a path to an optional, reproducible Docker Compose deployment after
  the native MVP; it must never become a prerequisite for the primary
  job-search flow.

### 3.2 Success criteria

| Metric | MVP target |
|---|---:|
| DOU discovery delay under normal upstream availability | <= 20 minutes |
| Duplicate rows for repeated source records | 0 |
| Duplicate confirmed Telegram sends for a notification key | 0 |
| Rules-only operation when AI is disabled | Fully functional |
| Source parser test execution | No live remote dependency |
| Secret values in source control/logs | 0 |
| Single-host recovery after worker restart | No loss of persisted jobs/outbox intent |
| Message rendering length | <= 4,096 Telegram characters |

## 4. Non-goals for MVP

- Applying to a vacancy automatically.
- A public web application, desktop GUI, or mobile client.
- Multi-user, team, SaaS, or multi-machine deployment.
- LinkedIn account login, cookie reuse, browser automation, CAPTCHA solving,
  proxy rotation, or any attempt to bypass source controls.
- A guarantee that any source is complete, current, or contractually stable.
- PDF/DOCX CV import; the input is structured YAML/JSON plus optional Markdown.
- Telegram commands, callback buttons, webhooks, or inbound polling.
- Exact-once external delivery; Telegram `sendMessage` cannot provide a client
  idempotency key.
- Worker Docker/Compose packaging and multi-architecture worker images in the
  native MVP release. The isolated optional JobSpy image remains supported.
- Vector search, embeddings, or automated application tracking.
- A durable enterprise scheduler such as Quartz/Hangfire.

## 5. Personas and primary journey

### Persona: individual .NET job seeker

The user owns the local machine, their job-search profile, a Telegram bot, and
optionally a GitHub Copilot subscription. They want low-noise, timely alerts and
must be able to audit why a job was selected.

### Primary journey

1. The user creates a Telegram bot, initiates its private chat, and provides the
   token through the selected platform's protected secret mechanism.
2. The user creates `profile.yaml` with explicit preferences and skill evidence.
3. The user enables DOU subscriptions and, only after accepting its risk, may
   enable the experimental JobSpy/LinkedIn subscription.
4. The worker starts and records source runs, normalized vacancies, and
   observations in SQLite.
5. Hard filters reject unsuitable jobs and deterministic rules score candidates.
6. When enabled, AI adds semantic criterion-level analysis. AI failure falls back
   to the rules-only policy.
7. A job that meets the active threshold creates a durable outbox item.
8. Telegram sends a concise, escaped summary once and the worker records the
   delivery outcome.

## 6. Functional requirements

### FR-01: Application lifecycle and scheduling

- The primary application is a .NET 10 Generic Host/Worker Service.
- The supported primary startup paths are `dotnet run` for development and a
  published native executable for ordinary Windows/macOS/Linux use. Neither path
  requires Docker.
- It exposes `run`, `run-once`, `doctor`, and `setup-telegram` operational
  commands, or equivalent safe documented entry points.
- A single orchestrator evaluates persisted source due times at least once a
  minute. It must not overlap an active scan of the same source subscription.
- Schedules and source backoff are stored in UTC. An app restart resumes from
  persisted state rather than assuming a clean run.
- Graceful shutdown stops new work, honours a bounded shutdown timeout, persists
  state, and releases execution leases.

### FR-02: DOU source

- DOU RSS feeds are the primary discovery transport:
  `https://jobs.dou.ua/vacancies/feeds/?category=.NET` and saved keyword feeds.
- A source subscription specifies category/keyword and optional UI-supported
  filters such as experience, city, remote, relocation, and search terms.
- Poll no more frequently than the source-provided cache interval; default to
  10-15 minutes plus jitter where the observed DOU feed cache is 600 seconds.
- Parse RSS title, link, GUID, publication date, description and source metadata.
- Remove tracking parameters such as `utm_*` and `from` when building canonical
  identity URLs.
- Optional HTML enrichment only fetches new/changed items or jobs missing needed
  data. It must be separately rate-limited and fixture-tested.
- A missing item from a bounded feed must not mark a job closed.

### FR-03: Experimental JobSpy / LinkedIn source

- JobSpy runs as an isolated Python/FastAPI sidecar and exposes only a versioned
  loopback/internal API to the .NET worker. The sidecar may run in an optional
  Docker container or in a separately managed local Python environment.
- DOU, SQLite, rules, Telegram, and optional Copilot analysis must start and
  work when JobSpy and Docker are absent.
- It is disabled by default and requires an explicit configuration opt-in.
- It does not receive the SQLite path, Telegram credentials, full profile/CV, or
  AI credentials.
- The API reports `Succeeded`, `Partial`, `Blocked`, or `Failed`, never treating
  a partial response as a full successful scan.
- A 403, 429, sign-in redirect, challenge, or scraper/parser incompatibility
  immediately pauses the subscription for a configured long backoff and exposes
  actionable health information.
- The product must not add evasion mechanisms. The connector can be removed
  without affecting the DOU pipeline.

### FR-04: Normalization and retention

- Every source record is converted into a source-neutral `Job` aggregate.
- Preserve raw provenance: source, source ID, exact source URL, canonical URL,
  source GUID, parser version, content hash, raw payload hash, query/subscription
  ID and retrieval timestamp.
- Store title, employer, description HTML and plain text, location(s), workplace
  mode, employment type, seniority, skills, categories, compensation, published
  timestamp and precision, and application URL separately.
- Keep first/last seen timestamps, last checked time, lifecycle state, status
  reason, and content revisions.
- Completed source-run/observation records, delivery attempts, and application
  events use a configurable 30-day default retention period with daily cleanup.
  Old terminal outbox payloads are scrubbed while their unique keys remain as
  deduplication tombstones. Core jobs, revisions, profiles, and evaluations are
  retained.

### FR-05: Deduplication and lifecycle

- Enforce unique `(source, source_job_id)` where a native ID exists.
- Otherwise enforce unique `(source, canonical_url)`.
- Store a versioned fallback SHA-256 fingerprint over normalized title, company,
  work mode/location, employment type, and bounded description excerpt.
- Potential cross-source matches create an auditable relationship; they must not
  merge or discard jobs automatically.
- A content hash change updates mutable job details and creates a revision.
- State changes to `possibly_stale`, `expired`, or `removed` require configured
  absence grace periods or authoritative evidence; feed absence alone is not
  authoritative.

### FR-06: Candidate profile

- MVP accepts validated YAML or JSON profile data and optional Markdown CV text.
- The structured profile contains target titles, skills with evidence/experience,
  role preferences, locations, remote policy, employment types, languages,
  salary expectations, excluded employers/keywords, and weights/thresholds.
- Markdown is supplementary evidence, not an unbounded instruction source.
- Contact details, full addresses, photos, government IDs, unrelated health data,
  and other unnecessary sensitive data must not be required.
- Validation must identify invalid or contradictory preferences before a scan.

### FR-07: Rules and scoring

- Hard filters yield explicit reason codes, such as `ExcludedEmployer`,
  `LocationMismatch`, `MissingMandatorySkill`, or `BelowSalaryFloor`.
- Deterministic score evaluates only job-relevant evidence and uses a versioned
  rubric. Default weights are: core .NET/C# 30, seniority 15, related stack 15,
  role responsibilities 15, location/language 10, domain 10, compensation 5.
- Rules produce evidence references and missing-data flags; never infer unstated
  years, salary, location, eligibility, or protected traits.
- `RulesOnlyThreshold` and `RulesAndAiThreshold` are independently configurable.
- Scores, thresholds, rules, and profile version are saved with each decision.

### FR-08: AI analysis

- `IJobAnalyzer` is a provider-neutral application boundary and supports
  capability discovery, cancellation, deadline, structured output, model/provider
  version, usage, warnings, and a schema/rubric version.
- GitHub Copilot SDK is the first implementation. It is optional and relies on a
  supported authentication mode and Copilot entitlement.
- Each analysis uses a new, constrained session. No filesystem, shell, browser,
  network, MCP, or arbitrary custom tools may be available to the model.
- A versioned application-owned prompt template and schema define allowed
  criteria. Vacancy/profile content is clearly delimited evidence-only data;
  tool permissions and evidence delimiters remain enforced in code.
- When AI is enabled with `FailStartupWhenModelUnavailable`, `run` and
  `run-once` validate the configured model before any scan. An unavailable
  model stops that command rather than silently selecting another model; the
  operator may explicitly select `auto` or disable strict startup validation to
  use rules-only fallback.
- Output must be validated locally. Invalid, timed-out, unavailable, refused, or
  over-budget AI output must not fail ingestion and must never be silently
  interpreted as a successful analysis.
- After a locally invalid structured submission, the system may make exactly one
  bounded corrective retry in a fresh restricted session with a
  validator-specific instruction. A second invalid result uses the same
  conservative fallback.
- Future adapters: Ollama via local HTTP API and OpenAI via the Responses API.
  They must implement the same domain contract without core changes.

### FR-09: Telegram notifications

- The service sends outbound messages to a configured, verified private chat.
- Initial setup retrieves/validates the chat ID only after the user starts the
  bot; bots cannot initiate a private chat.
- A qualifying job creates one durable notification key:
  `(destination_id, job_id, notification_version)`.
- Message fields: title, company, location/work mode, score and score mode,
  concise evidence/mismatch summary, credible compensation, age, and canonical
  HTTPS job link.
- User-facing Telegram text and HTML layout are maintained in a versioned
  application-owned template. The renderer keeps dynamic-value escaping,
  redaction, trusted-URL validation, and length enforcement outside that
  template.
- Escape all source-controlled fields for Telegram HTML. Never include the full
  vacancy, CV, contact details, recruiter email, or private notes by default.
- Enforce per-chat delivery <= 1 message/second and a bounded global queue.
- Handle 429 using Telegram `retry_after`. Persist the destination cooldown so
  it survives restart and also defers notifications enqueued after the 429.
  Network/5xx failures retry with a bounded jittered backoff; permanent
  403/chat errors disable the destination.
- A send timeout after a possibly accepted request becomes `Unknown`; default
  policy favours suppressing automatic duplicate delivery over duplicate alerts.

### FR-10: Operations and observability

- Use structured logs without secrets, CV bodies, full vacancy bodies, Telegram
  payloads, or private chat IDs.
- Record run duration/outcome, source counts, parser failures, backoffs,
  deduplication count, rule/AI result status, queue depth, delivery outcomes,
  SQLite busy retries, and last-success age.
- Provide liveness, readiness, and source/delivery freshness indicators.
- Provide a consistent manual SQLite backup command targeting an
  operator-selected encrypted/access-controlled location. The operator manages
  copy rotation and tests restore regularly; the application stores no default
  backup destination.
- A `doctor` command validates the selected deployment profile, application-data
  directory, secret provider, database writability, configured sources, Telegram
  connectivity, and optional AI/JobSpy health.

## 7. Data model

| Entity | Core responsibility |
|---|---|
| `CandidateProfile` | Versioned preferences, evidence, weights, thresholds |
| `SourceSubscription` | Source configuration, enabled state, interval, next due time |
| `SourceRun` | Attempt/run outcome, counts, cursor, backoff, diagnostics |
| `SourceCursor` | Source-specific durable incremental state |
| `Job` | Source-neutral vacancy identity and current normalized state |
| `JobRevision` | Content/field history after a meaningful change |
| `JobObservation` | Every source observation and raw provenance/hash |
| `JobPossibleDuplicate` | Reviewable cross-source relationship |
| `RuleEvaluation` | Versioned hard-filter and deterministic-score outcome |
| `AiAnalysis` | Provider/model/schema/rubric result and safely retained metadata |
| `NotificationOutbox` | Durable intended delivery, lease, state, and unique key |
| `DeliveryAttempt` | Attempt outcome, retry/unknown state, external message ID |
| `ApplicationEvent` | Privacy-safe operational audit event |

## 8. State machines

### Source subscription

```text
Enabled -> Running -> Enabled
Enabled -> BackingOff -> Enabled
Enabled -> Blocked -> ManuallyEnabled
Enabled -> Disabled
```

`Blocked` is mandatory for LinkedIn 403/429/challenge/sign-in cases. It requires
an explicit user decision to re-enable after the backoff expires.

### Job lifecycle

```text
Active -> PossiblyStale -> Expired or Removed
Active -> Updated (revision) -> Active
```

### Notification outbox

```text
Pending -> Leased -> Sent
                 -> Pending (transient retry)
                 -> Unknown
                 -> PermanentFailure
```

## 9. Security, privacy, and safety requirements

- Secrets are delivered through a platform-protected provider or a
  access-controlled secret file. Docker secrets are an optional Compose-mode
  mechanism; no secret is committed, baked into an image, written to SQLite, or
  logged.
- Python has least privilege and no access to .NET data/secrets except its narrow
  internal request contract.
- SQLite is stored in an OS-appropriate local application-data directory in
  native mode or one local Docker named volume in Compose mode; it is never on a
  network share. The .NET worker is its only writer.
- AI cloud use is opt-in and must declare data handling. Redact contact and
  unrelated sensitive information before cloud transmission.
- Local Ollama binds to loopback only unless separately secured.
- Prompt injection is controlled by isolated data, no tools, bounded input,
  fixed instructions, output schema validation, evidence IDs, and adversarial
  fixtures. No prompt wording alone is considered sufficient protection.
- Do not score age, gender, race, ethnicity, nationality, religion, disability,
  health, family status, address-derived traits, personality, or any proxy for a
  protected characteristic.
- AI is decision support for personal job search, not an autonomous hiring,
  rejection, or legal-eligibility system.

## 10. Non-functional requirements

| Area | Requirement |
|---|---|
| Runtime | Native Windows, macOS, Linux; optional Docker Compose profile |
| Framework | .NET 10 LTS |
| Database | EF Core SQLite with WAL, short transactions, bounded busy timeout |
| Availability | One local instance; source failure degrades but does not crash worker |
| Concurrency | Single SQLite writer; source fetches may be bounded-concurrent |
| Recovery | Idempotent persistence; leases expire after unexpected shutdown |
| Performance | DOU feed parsing and persistence must complete within its scan budget |
| Testability | Time, HTTP, filesystem boundary, AI provider, and Telegram transport injectable |
| Accessibility | No UI in MVP; messages must be concise and readable as plain text |
| Localization | UTC internally; preserve source locale/timestamps; message language configurable |

## 11. Configuration model

Safe defaults live in `appsettings.json`; secrets and environment-specific values
are external. Nested environment keys use `__`.

```yaml
worker:
  schedulerTickSeconds: 60
  shutdownTimeoutSeconds: 30
sources:
  dou:
    enabled: true
    defaultIntervalMinutes: 12
  linkedinJobSpy:
    enabled: false
    experimentalAcknowledged: false
    minimumIntervalMinutes: 60
scoring:
  rulesOnlyThreshold: 72
  rulesAndAiThreshold: 75
ai:
  enabled: false
  provider: copilot
  analysisTimeoutSeconds: 45
telegram:
  enabled: true
  perChatMessagesPerSecond: 1
```

## 12. Acceptance scenarios

1. A DOU RSS job first observed at 10:00 is normalized, scored, and, if it meets
   threshold, creates exactly one notification intent.
2. The same job appears in a later RSS feed with a new `utm_source`; it remains
   one job and produces no new notification.
3. A job description changes; the system creates a revision and may re-score it
   without duplicating the original `Job`.
4. AI is disabled; DOU ingestion, rules scoring, persistence, and Telegram still
   function.
5. Copilot times out or returns invalid structured data; the run succeeds with a
   recorded `AiUnavailable`/`InvalidOutput` state and conservative fallback.
6. JobSpy returns 429 or a sign-in redirect; the linked subscription becomes
   blocked, no evasion is attempted, and DOU continues normally.
7. Telegram returns 429; the outbox retains the same key and schedules retry from
   `retry_after`.
8. Worker crashes after Telegram might accept a request; outbox becomes `Unknown`
   and does not silently resend a duplicate.
9. Worker restart preserves existing jobs, source schedules, and pending outbox
   rows from the native application-data directory or optional mounted SQLite
   volume.
10. A parser fixture changes markup; the test fails with a useful diagnostic
    rather than silently generating malformed jobs.

## 13. External dependencies and source notes

| Dependency | Purpose | Constraint |
|---|---|---|
| [DOU Jobs](https://jobs.dou.ua/vacancies/feeds/?category=.NET) | Primary vacancy discovery | Public RSS, no documented SLA; respect cache/polling |
| [JobSpy](https://github.com/speedyapply/JobSpy) | Experimental LinkedIn fetch boundary | Pin exact version; scraper behavior can change |
| [LinkedIn User Agreement](https://www.linkedin.com/legal/user-agreement) | Source policy | No bypass/evasion; connector is opt-in and high risk |
| [GitHub Copilot SDK](https://docs.github.com/en/copilot/get-started/sdk-quickstart) | First optional AI provider | CLI agent runtime and entitlement/auth required |
| [Telegram Bot API](https://core.telegram.org/bots/api) | Notification transport | No external exactly-once guarantee |
| [EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations) | Local persistence | Single writer and migration constraints |
| [Ollama API](https://docs.ollama.com/api) | Future local AI adapter | Local model/hardware/license selection needed |
| [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs) | Future cloud AI adapter | Explicit opt-in and data policy required |

## 14. Release gates

The product cannot be called MVP-ready until every scenario in section 12 passes,
the checklist in [the implementation plan](IMPLEMENTATION_PLAN.md) is complete,
and the following manual checks pass:

- A clean Windows and macOS machine can run the DOU + rules + Telegram path from
  `dotnet run` or the published executable without Docker.
- Docker Compose restart and named-volume validation apply when the deferred
  Worker Compose profile is implemented; they do not gate the native MVP.
- `doctor` identifies a missing secret, inaccessible database, unavailable
  JobSpy, invalid Telegram destination, and unavailable optional Copilot adapter.
- A backup restores into an isolated volume and passes integrity checks.
- Logs reviewed from a normal and failure run contain no secret or sensitive
  content.
- The LinkedIn connector is disabled in default configuration and its warning is
  present in setup documentation.

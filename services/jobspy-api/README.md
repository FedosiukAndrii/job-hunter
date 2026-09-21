# JobSpy API

This is the Python 3.11+ sidecar for the experimental LinkedIn source. The
default Worker configuration enables this source. The .NET worker remains the
only SQLite writer.
The sidecar accepts no profile or CV, database path, Telegram token, cookie,
proxy, or credential fields.

The default Worker configuration enables LinkedIn and acknowledges its
experimental risk. A 403, 429, sign-in response, or challenge blocks this
source with a long backoff. Do not use this service to evade source controls.

## Native setup and run

From `services\jobspy-api` in PowerShell:

```powershell
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --require-hashes -r requirements-dev.lock
.\.venv\Scripts\python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8080 --no-server-header
```

On macOS or Linux:

```sh
python3.11 -m venv .venv
./.venv/bin/python -m pip install --require-hashes -r requirements-dev.lock
./.venv/bin/python -m uvicorn app.main:app --host 127.0.0.1 --port 8080 --no-server-header
```

Native execution must bind to `127.0.0.1`, not a public interface.

Run all tests without contacting a job board:

```powershell
.\.venv\Scripts\python.exe -m pytest
```

The tests inject a mock backend. Importing `app.main` does not import JobSpy;
the pinned package is imported only when the production backend performs a
search.

## API contract

All JSON names are camelCase. Search outcomes are `succeeded`, `partial`,
`blocked`, or `failed`. Mapped backend outcomes use HTTP 200 so the worker can
persist the complete source status envelope. Invalid or oversized requests use
HTTP 422 or 413 with the same `failed` envelope.

Endpoints:

- `GET /health`
- `GET /version`
- `POST /v1/search`

Example request:

```json
{
  "source": "linkedin",
  "searchTerm": ".NET",
  "location": "Ukraine",
  "resultsWanted": 20,
  "hoursOld": 48
}
```

`source` is an explicit allow-list and currently accepts only `linkedin`.
`searchTerm` and `location` are limited to 256 characters. `resultsWanted` is
1-50. `hoursOld` may be omitted and otherwise must be 1-8760; its default is 168.
Unknown request fields are rejected.

Example blocked response:

```json
{
  "status": "blocked",
  "jobs": [],
  "error": {
    "code": "linkedin_rate_limited",
    "message": "LinkedIn rate-limited the source. No bypass will be attempted.",
    "retryAfterSeconds": 86400,
    "action": "Wait for the long backoff, review the source state, and manually re-enable it if appropriate."
  },
  "providerVersion": "1.1.82"
}
```

The LinkedIn policy uses one backend worker and a 40-second service timeout.
An in-flight blocking library call cannot be interrupted safely, so it retains
the sole worker until it exits; no second LinkedIn call runs concurrently.

Safe optional environment settings:

| Name | Default | Bounds |
|---|---:|---:|
| `JOBSPY_LINKEDIN_TIMEOUT_SECONDS` | 40 | 1-300 |
| `JOBSPY_BLOCKED_RETRY_SECONDS` | 86400 | 3600-604800 |
| `JOBSPY_TRANSIENT_RETRY_SECONDS` | 900 | 60-86400 |
| `JOBSPY_MALFORMED_RETRY_SECONDS` | 3600 | 300-604800 |
| `JOBSPY_MAX_REQUEST_BYTES` | 16384 | 1024-1048576 |
| `JOBSPY_MAX_RESPONSE_BYTES` | 1900000 | 16384-2000000 |

## Rebuilding exact locks

The checked-in lock files contain exact transitive versions and hashes for
Python 3.11. Regenerate both with Python 3.11 and the pinned compiler:

```powershell
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install pip-tools==7.6.1
.\.venv\Scripts\python.exe -m piptools compile --generate-hashes --strip-extras --resolver=backtracking --output-file=requirements.lock requirements.in
.\.venv\Scripts\python.exe -m piptools compile --generate-hashes --strip-extras --resolver=backtracking --output-file=requirements-dev.lock requirements-dev.in
```

Review package and image changes before replacing a lock or base-image digest.

Audit the locked development environment with:

```powershell
.\.venv\Scripts\python.exe -m pip_audit --requirement requirements-dev.lock --disable-pip --strict --ignore-vuln PYSEC-2026-1604
```

The single ignored advisory is
`PYSEC-2026-1604`/`CVE-2025-46656` in `markdownify 0.13.1`.
`python-jobspy 1.1.82` requires `markdownify <0.14.0`, while the upstream fix is
`0.14.1`. The vulnerable conversion path expands malformed HTML heading names;
this adapter always requests `description_format="plain"`, which is covered by
the backend contract tests and does not invoke JobSpy's Markdown converter.
Keep this exception narrow and remove it as soon as `python-jobspy` accepts a
fixed `markdownify` release.

## Optional container

The image runs as UID/GID 10001 and binds to `0.0.0.0` only inside the
container. Publish it to host loopback:

```powershell
docker build -t job-hunter-jobspy .
docker run --rm -p 127.0.0.1:8080:8080 job-hunter-jobspy
```

Do not mount the .NET application-data directory or pass application secrets to
this container.

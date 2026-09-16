from __future__ import annotations

import asyncio
import threading
import time
from dataclasses import replace
from typing import Any

import httpx
import pytest
from fastapi.testclient import TestClient

from app.backend import (
    BackendFailure,
    BackendIssue,
    BackendSearchRequest,
    BackendSearchResult,
)
from app.config import Settings
from app.main import create_app
from app.models import SearchStatus


def valid_record(identifier: str = "123", **overrides: Any) -> dict[str, Any]:
    record: dict[str, Any] = {
        "site": "linkedin",
        "id": identifier,
        "job_url": f"https://www.linkedin.com/jobs/view/{identifier}?trk=test",
        "job_url_direct": "https://example.com/apply",
        "title": "Senior .NET Engineer",
        "company": "Example",
        "description": "Build safe C# services.",
        "location": "Remote",
        "date_posted": "2026-09-15",
        "job_type": "fulltime",
        "is_remote": True,
        "job_level": "Senior",
        "min_amount": 100_000,
        "max_amount": 140_000,
        "currency": "usd",
        "interval": "yearly",
    }
    record.update(overrides)
    return record


class StubBackend:
    def __init__(
        self,
        result: BackendSearchResult | None = None,
        error: Exception | None = None,
    ) -> None:
        self.result = result or BackendSearchResult(records=[])
        self.error = error
        self.calls: list[BackendSearchRequest] = []

    def search(self, request: BackendSearchRequest) -> BackendSearchResult:
        self.calls.append(request)
        if self.error is not None:
            raise self.error
        return self.result


def settings(**overrides: Any) -> Settings:
    return replace(Settings(), **overrides)


def search_payload(**overrides: Any) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "source": "linkedin",
        "searchTerm": ".NET",
        "location": "Ukraine",
        "resultsWanted": 20,
        "hoursOld": 48,
    }
    payload.update(overrides)
    return payload


def test_health_and_version_contracts_are_camel_case() -> None:
    with TestClient(create_app(StubBackend(), settings())) as client:
        health = client.get("/health")
        version = client.get("/version")

    assert health.status_code == 200
    assert health.json() == {
        "status": "ok",
        "allowedSources": ["linkedin"],
        "sourceConcurrency": {"linkedin": 1},
    }
    assert version.status_code == 200
    assert version.json()["serviceVersion"] == "0.1.0"
    assert version.json()["contractVersion"] == "v1"
    assert version.json()["providerName"] == "python-jobspy"
    assert "providerVersion" in version.json()


def test_search_maps_request_and_backend_record() -> None:
    backend = StubBackend(BackendSearchResult(records=[valid_record()]))
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload())

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "succeeded"
    assert "providerVersion" in body
    assert body["jobs"] == [
        {
            "source": "linkedin",
            "sourceJobId": "123",
            "sourceUrl": "https://www.linkedin.com/jobs/view/123?trk=test",
            "title": "Senior .NET Engineer",
            "company": "Example",
            "description": "Build safe C# services.",
            "location": "Remote",
            "workplaceMode": "remote",
            "employmentType": "full_time",
            "seniority": "Senior",
            "compensationMinimum": 100000.0,
            "compensationMaximum": 140000.0,
            "compensationCurrency": "USD",
            "compensationPeriod": "year",
            "publishedAtUtc": "2026-09-15T00:00:00Z",
            "applicationUrl": "https://example.com/apply",
        }
    ]
    call = backend.calls[0]
    assert call == BackendSearchRequest(
        source="linkedin",
        query=".NET",
        location="Ukraine",
        results_wanted=20,
        hours_old=48,
    )


@pytest.mark.parametrize("currency", ["USDX", "United States Dollar", "грн"])
def test_search_does_not_fabricate_currency_code(currency: str) -> None:
    backend = StubBackend(
        BackendSearchResult(records=[valid_record(currency=currency)])
    )
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload())

    job = response.json()["jobs"][0]
    assert "compensationCurrency" not in job


def test_query_alias_is_accepted_for_forward_compatibility() -> None:
    backend = StubBackend()
    payload = search_payload()
    payload["query"] = payload.pop("searchTerm")
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=payload)

    assert response.status_code == 200
    assert backend.calls[0].query == ".NET"


@pytest.mark.parametrize(
    "field",
    [
        "profile",
        "cv",
        "databasePath",
        "telegramToken",
        "cookies",
        "proxies",
        "credentials",
    ],
)
def test_search_rejects_sensitive_or_evasion_fields(field: str) -> None:
    backend = StubBackend()
    payload = search_payload(**{field: "must-not-be-accepted"})
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=payload)

    assert response.status_code == 422
    assert response.json()["status"] == "failed"
    assert response.json()["error"]["code"] == "invalid_request"
    assert backend.calls == []


@pytest.mark.parametrize(
    "overrides",
    [
        {"source": "indeed"},
        {"searchTerm": ""},
        {"searchTerm": "x" * 257},
        {"location": "x" * 257},
        {"resultsWanted": 0},
        {"resultsWanted": 51},
        {"resultsWanted": "20"},
        {"hoursOld": 0},
        {"hoursOld": None},
        {"hoursOld": "48"},
        {"hoursOld": 8_761},
    ],
)
def test_search_rejects_unsupported_or_out_of_bounds_input(
    overrides: dict[str, Any],
) -> None:
    backend = StubBackend()
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload(**overrides))

    assert response.status_code == 422
    body = response.json()
    assert body["status"] == "failed"
    assert body["jobs"] == []
    assert body["error"]["code"] == "invalid_request"
    assert "details" in body["error"]
    assert backend.calls == []


@pytest.mark.parametrize(
    "snake_case_field",
    ["search_term", "results_wanted", "hours_old"],
)
def test_search_rejects_snake_case_json(snake_case_field: str) -> None:
    backend = StubBackend()
    payload = search_payload()
    camel_case_field = {
        "search_term": "searchTerm",
        "results_wanted": "resultsWanted",
        "hours_old": "hoursOld",
    }[snake_case_field]
    payload[snake_case_field] = payload.pop(camel_case_field)
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=payload)

    assert response.status_code == 422
    assert response.json()["error"]["code"] == "invalid_request"
    assert backend.calls == []


def test_search_rejects_oversized_request_before_backend_call() -> None:
    backend = StubBackend()
    application = create_app(backend, settings(max_request_bytes=1_024))
    with TestClient(application) as client:
        response = client.post(
            "/v1/search",
            content=b"{" + (b"x" * 2_000) + b"}",
            headers={"content-type": "application/json"},
        )

    assert response.status_code == 413
    assert response.json()["error"]["code"] == "request_too_large"
    assert backend.calls == []


def test_search_rejects_oversized_streamed_request() -> None:
    backend = StubBackend()
    application = create_app(backend, settings(max_request_bytes=1_024))

    async def content() -> Any:
        yield b"{" + (b"x" * 2_000) + b"}"

    async def post() -> httpx.Response:
        async with application.router.lifespan_context(application):
            async with httpx.AsyncClient(
                transport=httpx.ASGITransport(app=application),
                base_url="http://test",
            ) as client:
                return await client.post(
                    "/v1/search",
                    content=content(),
                    headers={"content-type": "application/json"},
                )

    response = asyncio.run(post())

    assert response.status_code == 413
    assert response.json()["error"]["code"] == "request_too_large"
    assert backend.calls == []


@pytest.mark.parametrize(
    ("code", "message"),
    [
        ("linkedin_forbidden", "LinkedIn returned HTTP 403."),
        ("linkedin_rate_limited", "LinkedIn returned HTTP 429."),
        ("linkedin_sign_in", "LinkedIn redirected to sign-in."),
        ("linkedin_challenge", "LinkedIn returned a challenge."),
    ],
)
def test_access_control_failures_are_blocked(code: str, message: str) -> None:
    issue = BackendIssue(
        status=SearchStatus.BLOCKED,
        code=code,
        message=message,
        retry_after_seconds=86_400,
        action="Review the source and manually re-enable it after backoff.",
    )
    backend = StubBackend(error=BackendFailure(issue))
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload())

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "blocked"
    assert body["jobs"] == []
    assert body["error"] == {
        "code": code,
        "message": message,
        "retryAfterSeconds": 86400,
        "action": "Review the source and manually re-enable it after backoff.",
    }


def test_parser_incompatibility_is_failed_with_backoff_hint() -> None:
    issue = BackendIssue(
        status=SearchStatus.FAILED,
        code="parser_incompatible",
        message="The parser is incompatible.",
        retry_after_seconds=86_400,
        action="Update the pinned adapter before retrying.",
    )
    with TestClient(
        create_app(StubBackend(error=BackendFailure(issue)), settings())
    ) as client:
        response = client.post("/v1/search", json=search_payload())

    body = response.json()
    assert body["status"] == "failed"
    assert body["error"]["code"] == "parser_incompatible"
    assert body["error"]["retryAfterSeconds"] == 86_400


def test_backend_partial_result_preserves_valid_jobs() -> None:
    issue = BackendIssue(
        status=SearchStatus.PARTIAL,
        code="backend_partial",
        message="The backend stopped after returning some jobs.",
        retry_after_seconds=900,
        action="Retry after backoff.",
    )
    backend = StubBackend(
        BackendSearchResult(records=[valid_record()], issue=issue)
    )
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload())

    body = response.json()
    assert body["status"] == "partial"
    assert len(body["jobs"]) == 1
    assert body["error"]["code"] == "backend_partial"
    assert body["error"]["retryAfterSeconds"] == 900


def test_malformed_backend_record_makes_result_partial() -> None:
    malformed = valid_record("bad", company=None)
    backend = StubBackend(
        BackendSearchResult(records=[valid_record("good"), malformed])
    )
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload())

    body = response.json()
    assert body["status"] == "partial"
    assert [job["sourceJobId"] for job in body["jobs"]] == ["good"]
    assert body["error"]["code"] == "malformed_backend_data"
    assert body["error"]["retryAfterSeconds"] == 3_600


def test_only_malformed_backend_data_fails() -> None:
    backend = StubBackend(
        BackendSearchResult(records=[{"site": "linkedin", "id": "bad"}])
    )
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload())

    body = response.json()
    assert body["status"] == "failed"
    assert body["jobs"] == []
    assert body["error"]["code"] == "malformed_backend_data"


def test_malformed_backend_collection_fails_safely() -> None:
    backend = StubBackend(BackendSearchResult(records=None))  # type: ignore[arg-type]
    with TestClient(create_app(backend, settings())) as client:
        response = client.post("/v1/search", json=search_payload())

    body = response.json()
    assert body["status"] == "failed"
    assert body["jobs"] == []
    assert body["error"]["code"] == "malformed_backend_data"


def test_source_timeout_is_failed_with_retry_hint() -> None:
    class SlowBackend:
        def search(self, _: BackendSearchRequest) -> BackendSearchResult:
            time.sleep(0.05)
            return BackendSearchResult(records=[])

    with TestClient(
        create_app(
            SlowBackend(),
            settings(
                linkedin_timeout_seconds=0.01,
                transient_retry_after_seconds=123,
            ),
        )
    ) as client:
        response = client.post("/v1/search", json=search_payload())

    body = response.json()
    assert body["status"] == "failed"
    assert body["error"]["code"] == "timeout"
    assert body["error"]["retryAfterSeconds"] == 123


def test_linkedin_backend_concurrency_is_one() -> None:
    class MeasuringBackend:
        def __init__(self) -> None:
            self.active = 0
            self.maximum_active = 0
            self.lock = threading.Lock()

        def search(self, _: BackendSearchRequest) -> BackendSearchResult:
            with self.lock:
                self.active += 1
                self.maximum_active = max(self.maximum_active, self.active)
            time.sleep(0.03)
            with self.lock:
                self.active -= 1
            return BackendSearchResult(records=[])

    backend = MeasuringBackend()
    application = create_app(backend, settings(linkedin_timeout_seconds=1))

    async def run_requests() -> list[httpx.Response]:
        async with application.router.lifespan_context(application):
            async with httpx.AsyncClient(
                transport=httpx.ASGITransport(app=application),
                base_url="http://test",
            ) as client:
                return await asyncio.gather(
                    client.post("/v1/search", json=search_payload()),
                    client.post("/v1/search", json=search_payload()),
                )

    responses = asyncio.run(run_requests())

    assert [response.status_code for response in responses] == [200, 200]
    assert backend.maximum_active == 1


def test_response_size_limit_returns_partial() -> None:
    records = [
        valid_record(str(index), description="x" * 10_000)
        for index in range(4)
    ]
    backend = StubBackend(BackendSearchResult(records=records))
    with TestClient(
        create_app(backend, settings(max_response_bytes=16_384))
    ) as client:
        response = client.post(
            "/v1/search",
            json=search_payload(resultsWanted=4),
        )

    body = response.json()
    assert body["status"] == "partial"
    assert 0 < len(body["jobs"]) < 4
    assert body["error"]["code"] == "response_limit"

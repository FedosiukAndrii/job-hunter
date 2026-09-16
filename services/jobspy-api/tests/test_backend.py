from __future__ import annotations

import logging
import sys
from types import ModuleType
from typing import Any

import pytest

from app.backend import (
    BackendFailure,
    BackendSearchRequest,
    PythonJobSpyBackend,
)
from app.models import SearchStatus


class FakeFrame:
    def __init__(self, records: list[dict[str, Any]]) -> None:
        self._records = records

    def to_dict(self, *, orient: str) -> list[dict[str, Any]]:
        assert orient == "records"
        return self._records


def request() -> BackendSearchRequest:
    return BackendSearchRequest(
        source="linkedin",
        query=".NET",
        location="Remote",
        results_wanted=10,
        hours_old=24,
    )


def install_fake_jobspy(
    monkeypatch: pytest.MonkeyPatch,
    scrape_jobs: Any,
) -> None:
    module = ModuleType("jobspy")
    module.scrape_jobs = scrape_jobs  # type: ignore[attr-defined]
    monkeypatch.setitem(sys.modules, "jobspy", module)


def test_backend_calls_lazy_jobspy_boundary_without_evasion_fields(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    captured: dict[str, Any] = {}

    def scrape_jobs(**kwargs: Any) -> FakeFrame:
        captured.update(kwargs)
        return FakeFrame([])

    install_fake_jobspy(monkeypatch, scrape_jobs)
    result = PythonJobSpyBackend().search(request())

    assert result.records == []
    assert captured == {
        "site_name": "linkedin",
        "search_term": ".NET",
        "location": "Remote",
        "results_wanted": 10,
        "hours_old": 24,
        "description_format": "plain",
        "linkedin_fetch_description": True,
        "verbose": 0,
    }
    assert not {
        "cookies",
        "proxies",
        "credentials",
        "user_agent",
    }.intersection(captured)


@pytest.mark.parametrize(
    ("log_message", "expected_code"),
    [
        (
            "429 Response - Blocked by LinkedIn for too many requests",
            "linkedin_rate_limited",
        ),
        ("LinkedIn response status code 403", "linkedin_forbidden"),
        ("Redirected to /login challenge", "linkedin_sign_in_or_challenge"),
    ],
)
def test_backend_classifies_access_control_logs_as_blocked(
    monkeypatch: pytest.MonkeyPatch,
    log_message: str,
    expected_code: str,
) -> None:
    def scrape_jobs(**_: Any) -> FakeFrame:
        logging.getLogger("JobSpy:LinkedIn").error(log_message)
        return FakeFrame([])

    install_fake_jobspy(monkeypatch, scrape_jobs)

    with pytest.raises(BackendFailure) as raised:
        PythonJobSpyBackend().search(request())

    assert raised.value.issue.status is SearchStatus.BLOCKED
    assert raised.value.issue.code == expected_code
    assert raised.value.issue.retry_after_seconds == 86_400
    assert "bypass" in raised.value.issue.message.lower() or (
        "bypass" in raised.value.issue.action.lower()
    )


def test_backend_classifies_parser_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def scrape_jobs(**_: Any) -> FakeFrame:
        raise AttributeError("NoneType parser selector changed")

    install_fake_jobspy(monkeypatch, scrape_jobs)

    with pytest.raises(BackendFailure) as raised:
        PythonJobSpyBackend().search(request())

    assert raised.value.issue.status is SearchStatus.FAILED
    assert raised.value.issue.code == "parser_incompatible"


def test_backend_detects_sign_in_redirect_from_jobspy_session(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    module = ModuleType("jobspy")

    class Response:
        status_code = 302
        headers = {"Location": "https://www.linkedin.com/authwall"}
        url = "https://www.linkedin.com/authwall"

    class Session:
        def get(self, *_: Any, **__: Any) -> Response:
            return Response()

    class LinkedIn:
        def __init__(self, *_: Any, **__: Any) -> None:
            self.session = Session()

    def scrape_jobs(**_: Any) -> FakeFrame:
        scraper = module.LinkedIn()  # type: ignore[attr-defined]
        scraper.session.get("https://www.linkedin.com/jobs")
        return FakeFrame([])

    module.LinkedIn = LinkedIn  # type: ignore[attr-defined]
    module.scrape_jobs = scrape_jobs  # type: ignore[attr-defined]
    monkeypatch.setitem(sys.modules, "jobspy", module)

    with pytest.raises(BackendFailure) as raised:
        PythonJobSpyBackend().search(request())

    assert raised.value.issue.status is SearchStatus.BLOCKED
    assert raised.value.issue.code == "linkedin_sign_in_or_challenge"


def test_backend_marks_records_partial_after_non_blocking_error(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def scrape_jobs(**_: Any) -> FakeFrame:
        logging.getLogger("JobSpy:LinkedIn").error("connection reset")
        return FakeFrame([{"site": "linkedin"}])

    install_fake_jobspy(monkeypatch, scrape_jobs)
    result = PythonJobSpyBackend().search(request())

    assert len(result.records) == 1
    assert result.issue is not None
    assert result.issue.status is SearchStatus.PARTIAL
    assert result.issue.code == "backend_unavailable"


def test_backend_rejects_incompatible_result_container(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def scrape_jobs(**_: Any) -> object:
        return object()

    install_fake_jobspy(monkeypatch, scrape_jobs)

    with pytest.raises(BackendFailure) as raised:
        PythonJobSpyBackend().search(request())

    assert raised.value.issue.status is SearchStatus.FAILED
    assert raised.value.issue.code == "malformed_backend_data"

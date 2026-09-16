from __future__ import annotations

import logging
import threading
from dataclasses import dataclass, replace
from importlib import import_module, metadata
from types import ModuleType
from typing import Any, Callable, Mapping, Protocol, Sequence

from app.models import SearchStatus

_JOBSPY_LOGGER_NAME = "JobSpy:LinkedIn"
_LOGGER_SWAP_LOCK = threading.Lock()
_BUILTIN_PROVIDER_ERRORS = (
    AttributeError,
    KeyError,
    OSError,
    OverflowError,
    RuntimeError,
    TypeError,
    ValueError,
)
_RESULT_SERIALIZATION_ERRORS = (
    AttributeError,
    KeyError,
    OverflowError,
    TypeError,
    ValueError,
)


@dataclass(frozen=True, slots=True)
class BackendSearchRequest:
    source: str
    query: str
    location: str | None
    results_wanted: int
    hours_old: int


@dataclass(frozen=True, slots=True)
class BackendIssue:
    status: SearchStatus
    code: str
    message: str
    retry_after_seconds: int
    action: str


@dataclass(frozen=True, slots=True)
class BackendSearchResult:
    records: Sequence[Mapping[str, Any]]
    issue: BackendIssue | None = None


class BackendFailure(Exception):
    def __init__(self, issue: BackendIssue) -> None:
        super().__init__(issue.code)
        self.issue = issue


class JobSearchBackend(Protocol):
    def search(self, request: BackendSearchRequest) -> BackendSearchResult:
        """Run one synchronous search without owning timeout or concurrency."""


class _BoundedLogCapture(logging.Handler):
    def __init__(self) -> None:
        super().__init__(level=logging.ERROR)
        self.messages: list[str] = []

    def emit(self, record: logging.LogRecord) -> None:
        try:
            message = record.getMessage()
        except (TypeError, ValueError):
            message = type(record.msg).__name__
        self.messages.append(message[:4_096])


class _GuardedSession:
    def __init__(self, inner: Any, signals: list[str]) -> None:
        self._inner = inner
        self._signals = signals

    def __getattr__(self, name: str) -> Any:
        return getattr(self._inner, name)

    def get(self, *args: Any, **kwargs: Any) -> Any:
        response = self._inner.get(*args, **kwargs)
        status_code = getattr(response, "status_code", None)
        if status_code == 403:
            self._signals.append("403")
        elif status_code == 429:
            self._signals.append("429")

        headers = getattr(response, "headers", {}) or {}
        location = ""
        if hasattr(headers, "get"):
            location = str(headers.get("location") or headers.get("Location") or "")
        response_url = str(getattr(response, "url", "") or "")
        redirect_text = f"{location} {response_url}".lower()
        if any(
            marker in redirect_text
            for marker in (
                "/login",
                "/signin",
                "/signup",
                "/authwall",
                "/challenge",
                "/checkpoint",
            )
        ):
            self._signals.append("sign-in or challenge")
        return response


class PythonJobSpyBackend:
    """The only module boundary that imports and calls python-jobspy."""

    def __init__(
        self,
        blocked_retry_after_seconds: int = 86_400,
        transient_retry_after_seconds: int = 900,
    ) -> None:
        self._blocked_retry_after_seconds = blocked_retry_after_seconds
        self._transient_retry_after_seconds = transient_retry_after_seconds

    def search(self, request: BackendSearchRequest) -> BackendSearchResult:
        capture = _BoundedLogCapture()
        logger = logging.getLogger(_JOBSPY_LOGGER_NAME)

        with _LOGGER_SWAP_LOCK:
            previous_handlers = list(logger.handlers)
            previous_level = logger.level
            previous_propagate = logger.propagate
            logger.handlers = [capture]
            logger.setLevel(logging.ERROR)
            logger.propagate = False
            try:
                try:
                    jobspy = import_module("jobspy")
                except (ImportError, OSError) as error:
                    raise BackendFailure(
                        self._failed_issue(
                            "backend_dependency_unavailable",
                            "The pinned python-jobspy backend is unavailable.",
                            "Install the exact locked runtime dependencies before retrying.",
                        )
                    ) from error

                provider_errors = self._provider_error_types(jobspy)
                access_signals, restore_linkedin = self._guard_linkedin_session(jobspy)
                try:
                    frame = jobspy.scrape_jobs(
                        site_name=request.source,
                        search_term=request.query,
                        location=request.location,
                        results_wanted=request.results_wanted,
                        hours_old=request.hours_old,
                        description_format="plain",
                        linkedin_fetch_description=True,
                        verbose=0,
                    )
                except provider_errors as error:
                    issue = self._classify_failure(
                        [f"{type(error).__name__}: {str(error)[:4_096]}"]
                        + access_signals
                        + capture.messages
                    )
                    raise BackendFailure(issue) from error
                finally:
                    restore_linkedin()
            finally:
                logger.handlers = previous_handlers
                logger.setLevel(previous_level)
                logger.propagate = previous_propagate

        try:
            records = frame.to_dict(orient="records")
        except _RESULT_SERIALIZATION_ERRORS as error:
            raise BackendFailure(
                self._failed_issue(
                    "malformed_backend_data",
                    "JobSpy returned an incompatible result container.",
                    "Verify the pinned python-jobspy version and adapter compatibility.",
                )
            ) from error

        if not isinstance(records, list):
            raise BackendFailure(
                self._failed_issue(
                    "malformed_backend_data",
                    "JobSpy returned an incompatible result container.",
                    "Verify the pinned python-jobspy version and adapter compatibility.",
                )
            )

        diagnostic_messages = access_signals + capture.messages
        issue = (
            self._classify_failure(diagnostic_messages)
            if diagnostic_messages
            else None
        )
        if issue is not None:
            if issue.status is SearchStatus.BLOCKED:
                raise BackendFailure(issue)
            if records:
                issue = replace(issue, status=SearchStatus.PARTIAL)
            else:
                raise BackendFailure(issue)

        return BackendSearchResult(records=records, issue=issue)

    def _provider_error_types(
        self,
        jobspy_module: ModuleType,
    ) -> tuple[type[BaseException], ...]:
        if getattr(jobspy_module, "__path__", None) is None:
            return _BUILTIN_PROVIDER_ERRORS

        try:
            exception_module = import_module("jobspy.exception")
        except (ImportError, OSError) as error:
            raise BackendFailure(
                self._failed_issue(
                    "backend_dependency_unavailable",
                    "The pinned python-jobspy backend is incomplete.",
                    "Install the exact locked runtime dependencies before retrying.",
                )
            ) from error

        linkedin_exception = getattr(exception_module, "LinkedInException", None)
        if (
            not isinstance(linkedin_exception, type)
            or not issubclass(linkedin_exception, Exception)
        ):
            raise BackendFailure(
                self._failed_issue(
                    "backend_dependency_incompatible",
                    "The pinned python-jobspy exception contract is incompatible.",
                    "Restore the exact locked python-jobspy dependency before retrying.",
                )
            )

        return (*_BUILTIN_PROVIDER_ERRORS, linkedin_exception)

    @staticmethod
    def _guard_linkedin_session(
        jobspy_module: ModuleType,
    ) -> tuple[list[str], Callable[[], None]]:
        signals: list[str] = []
        original_linkedin = getattr(jobspy_module, "LinkedIn", None)
        if original_linkedin is None:
            return signals, lambda: None

        def guarded_linkedin(*args: Any, **kwargs: Any) -> Any:
            scraper = original_linkedin(*args, **kwargs)
            scraper.session = _GuardedSession(scraper.session, signals)
            return scraper

        jobspy_module.LinkedIn = guarded_linkedin

        def restore() -> None:
            jobspy_module.LinkedIn = original_linkedin

        return signals, restore

    def _classify_failure(self, messages: Sequence[str]) -> BackendIssue:
        text = " ".join(messages).lower()
        if any(
            marker in text
            for marker in ("429", "too many requests", "rate limit", "ratelimit")
        ):
            return BackendIssue(
                status=SearchStatus.BLOCKED,
                code="linkedin_rate_limited",
                message="LinkedIn rate-limited the source. No bypass will be attempted.",
                retry_after_seconds=self._blocked_retry_after_seconds,
                action=(
                    "Wait for the long backoff, review the source state, and manually "
                    "re-enable it if appropriate."
                ),
            )
        if any(marker in text for marker in ("403", "forbidden", "access denied")):
            return BackendIssue(
                status=SearchStatus.BLOCKED,
                code="linkedin_forbidden",
                message="LinkedIn denied access. No bypass will be attempted.",
                retry_after_seconds=self._blocked_retry_after_seconds,
                action=(
                    "Wait for the long backoff, review the source state, and manually "
                    "re-enable it if appropriate."
                ),
            )
        if any(
            marker in text
            for marker in (
                "sign-in",
                "signin",
                "signup",
                "login",
                "authwall",
                "challenge",
                "checkpoint",
            )
        ):
            return BackendIssue(
                status=SearchStatus.BLOCKED,
                code="linkedin_sign_in_or_challenge",
                message="LinkedIn returned a sign-in or challenge response.",
                retry_after_seconds=self._blocked_retry_after_seconds,
                action=(
                    "Do not attempt to bypass the response. Review the source and "
                    "manually re-enable it only after the backoff."
                ),
            )
        if any(
            marker in text
            for marker in (
                "linkedinexception",
                "attributeerror",
                "nonetype",
                "selector",
                "beautifulsoup",
                "parse error",
                "parser",
            )
        ):
            return BackendIssue(
                status=SearchStatus.FAILED,
                code="parser_incompatible",
                message="The pinned JobSpy parser is incompatible with the response.",
                retry_after_seconds=self._blocked_retry_after_seconds,
                action=(
                    "Verify the pinned python-jobspy version and update the adapter "
                    "before retrying."
                ),
            )
        return self._failed_issue(
            "backend_unavailable",
            "JobSpy could not complete the LinkedIn search.",
            "Retry after the bounded backoff and inspect privacy-safe service logs.",
        )

    def _failed_issue(self, code: str, message: str, action: str) -> BackendIssue:
        return BackendIssue(
            status=SearchStatus.FAILED,
            code=code,
            message=message,
            retry_after_seconds=self._transient_retry_after_seconds,
            action=action,
        )


def provider_version() -> str:
    try:
        return metadata.version("python-jobspy")
    except metadata.PackageNotFoundError:
        return "not-installed"

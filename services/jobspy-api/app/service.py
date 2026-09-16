from __future__ import annotations

import asyncio
import json
import logging
from concurrent.futures import ThreadPoolExecutor

from app.backend import (
    BackendFailure,
    BackendIssue,
    BackendSearchRequest,
    BackendSearchResult,
    JobSearchBackend,
)
from app.config import Settings
from app.models import ErrorEnvelope, Job, SearchRequest, SearchResponse, SearchStatus
from app.normalization import normalize_records

_LOGGER = logging.getLogger("jobspy_api")
_BACKEND_OPERATION_ERRORS = (
    AttributeError,
    KeyError,
    OSError,
    OverflowError,
    RuntimeError,
    TypeError,
    ValueError,
)


class SearchCoordinator:
    def __init__(
        self,
        backend: JobSearchBackend,
        timeout_seconds: float,
    ) -> None:
        self._backend = backend
        self._timeout_seconds = timeout_seconds
        self._executor = ThreadPoolExecutor(
            max_workers=1,
            thread_name_prefix="jobspy-linkedin",
        )
        self._closed = False

    async def search(self, request: BackendSearchRequest) -> BackendSearchResult:
        if self._closed:
            raise RuntimeError("search coordinator is closed")
        loop = asyncio.get_running_loop()
        future = loop.run_in_executor(self._executor, self._backend.search, request)
        try:
            return await asyncio.wait_for(future, timeout=self._timeout_seconds)
        except TimeoutError:
            future.cancel()
            raise

    def close(self) -> None:
        self._closed = True
        self._executor.shutdown(wait=False, cancel_futures=True)


class SearchService:
    def __init__(
        self,
        backend: JobSearchBackend,
        settings: Settings,
        provider_version: str,
    ) -> None:
        self._settings = settings
        self._provider_version = provider_version
        self._coordinator = SearchCoordinator(
            backend,
            settings.linkedin_timeout_seconds,
        )

    async def search(self, request: SearchRequest) -> SearchResponse:
        backend_request = BackendSearchRequest(
            source=request.source.value,
            query=request.search_term,
            location=request.location,
            results_wanted=request.results_wanted,
            hours_old=request.hours_old,
        )
        try:
            result = await self._coordinator.search(backend_request)
        except TimeoutError:
            return self._error_response(
                BackendIssue(
                    status=SearchStatus.FAILED,
                    code="timeout",
                    message="The LinkedIn search exceeded its configured timeout.",
                    retry_after_seconds=self._settings.transient_retry_after_seconds,
                    action="Retry after the bounded backoff.",
                )
            )
        except BackendFailure as error:
            return self._error_response(error.issue)
        except _BACKEND_OPERATION_ERRORS as error:
            _LOGGER.error(
                "JobSpy backend failed with exceptionType=%s",
                type(error).__name__,
            )
            return self._error_response(
                BackendIssue(
                    status=SearchStatus.FAILED,
                    code="backend_error",
                    message="The JobSpy backend failed unexpectedly.",
                    retry_after_seconds=self._settings.transient_retry_after_seconds,
                    action=(
                        "Retry after the bounded backoff and inspect privacy-safe "
                        "service logs."
                    ),
                )
            )

        if result.issue is not None and result.issue.status in {
            SearchStatus.BLOCKED,
            SearchStatus.FAILED,
        }:
            return self._error_response(result.issue)

        try:
            normalized = normalize_records(result.records, request.results_wanted)
        except (TypeError, ValueError, OverflowError):
            return self._error_response(
                BackendIssue(
                    status=SearchStatus.FAILED,
                    code="malformed_backend_data",
                    message="JobSpy returned an incompatible record collection.",
                    retry_after_seconds=self._settings.malformed_retry_after_seconds,
                    action=(
                        "Verify the pinned python-jobspy version and adapter "
                        "compatibility before retrying."
                    ),
                )
            )
        if normalized.invalid_count:
            status = SearchStatus.PARTIAL if normalized.jobs else SearchStatus.FAILED
            return self._jobs_response(
                status=status,
                jobs=normalized.jobs,
                issue=BackendIssue(
                    status=status,
                    code="malformed_backend_data",
                    message=(
                        "JobSpy returned one or more malformed records; invalid "
                        "records were discarded."
                    ),
                    retry_after_seconds=self._settings.malformed_retry_after_seconds,
                    action=(
                        "Verify the pinned python-jobspy version and adapter "
                        "compatibility before retrying."
                    ),
                ),
            )

        if normalized.truncated_count:
            return self._jobs_response(
                status=SearchStatus.PARTIAL,
                jobs=normalized.jobs,
                issue=BackendIssue(
                    status=SearchStatus.PARTIAL,
                    code="backend_result_limit",
                    message="JobSpy returned more records than the requested limit.",
                    retry_after_seconds=self._settings.malformed_retry_after_seconds,
                    action="Verify the pinned backend and adapter result limit.",
                ),
            )

        if result.issue is not None:
            status = SearchStatus.PARTIAL if normalized.jobs else SearchStatus.FAILED
            return self._jobs_response(
                status=status,
                jobs=normalized.jobs,
                issue=BackendIssue(
                    status=status,
                    code=result.issue.code,
                    message=result.issue.message,
                    retry_after_seconds=result.issue.retry_after_seconds,
                    action=result.issue.action,
                ),
            )

        return self._jobs_response(
            status=SearchStatus.SUCCEEDED,
            jobs=normalized.jobs,
            issue=None,
        )

    def close(self) -> None:
        self._coordinator.close()

    def _jobs_response(
        self,
        *,
        status: SearchStatus,
        jobs: list[Job],
        issue: BackendIssue | None,
    ) -> SearchResponse:
        fitted_jobs, omitted_count = self._fit_response_budget(jobs)
        if omitted_count:
            status = SearchStatus.PARTIAL if fitted_jobs else SearchStatus.FAILED
            issue = BackendIssue(
                status=status,
                code="response_limit",
                message="Results were omitted to keep the response within its byte limit.",
                retry_after_seconds=self._settings.malformed_retry_after_seconds,
                action="Reduce resultsWanted before retrying.",
            )
        return SearchResponse(
            status=status,
            jobs=fitted_jobs,
            error=self._error_envelope(issue) if issue else None,
            provider_version=self._provider_version,
        )

    def _error_response(self, issue: BackendIssue) -> SearchResponse:
        return SearchResponse(
            status=issue.status,
            jobs=[],
            error=self._error_envelope(issue),
            provider_version=self._provider_version,
        )

    @staticmethod
    def _error_envelope(issue: BackendIssue) -> ErrorEnvelope:
        return ErrorEnvelope(
            code=issue.code,
            message=issue.message,
            retry_after_seconds=issue.retry_after_seconds,
            action=issue.action,
        )

    def _fit_response_budget(self, jobs: list[Job]) -> tuple[list[Job], int]:
        fitted: list[Job] = []
        estimated_bytes = 512
        for job in jobs:
            serialized = json.dumps(
                job.model_dump(mode="json", by_alias=True, exclude_none=True),
                ensure_ascii=False,
                separators=(",", ":"),
            ).encode("utf-8")
            if estimated_bytes + len(serialized) + 1 > self._settings.max_response_bytes:
                return fitted, len(jobs) - len(fitted)
            fitted.append(job)
            estimated_bytes += len(serialized) + 1
        return fitted, 0

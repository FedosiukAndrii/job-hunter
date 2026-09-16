from __future__ import annotations

from contextlib import asynccontextmanager
from typing import Any

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app.backend import JobSearchBackend, PythonJobSpyBackend, provider_version
from app.config import CONTRACT_VERSION, SERVICE_VERSION, Settings
from app.models import (
    ErrorDetail,
    ErrorEnvelope,
    HealthResponse,
    SearchRequest,
    SearchResponse,
    SearchStatus,
    VersionResponse,
)
from app.service import SearchService


class RequestBodyLimitMiddleware:
    def __init__(self, app: Any, maximum_bytes: int) -> None:
        self._app = app
        self._maximum_bytes = maximum_bytes

    async def __call__(self, scope: dict[str, Any], receive: Any, send: Any) -> None:
        if scope["type"] != "http" or scope.get("path") != "/v1/search":
            await self._app(scope, receive, send)
            return

        headers = dict(scope.get("headers", []))
        content_length = headers.get(b"content-length")
        if content_length is not None:
            try:
                parsed_content_length = int(content_length)
                if (
                    parsed_content_length < 0
                    or parsed_content_length > self._maximum_bytes
                ):
                    await self._send_too_large(scope, receive, send)
                    return
            except ValueError:
                await self._send_too_large(scope, receive, send)
                return

        body = bytearray()
        more_body = True
        while more_body:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            if message["type"] != "http.request":
                continue
            body.extend(message.get("body", b""))
            if len(body) > self._maximum_bytes:
                await self._send_too_large(scope, receive, send)
                return
            more_body = bool(message.get("more_body", False))

        replayed = False

        async def replay_body() -> dict[str, Any]:
            nonlocal replayed
            if not replayed:
                replayed = True
                return {
                    "type": "http.request",
                    "body": bytes(body),
                    "more_body": False,
                }
            return await receive()

        await self._app(scope, replay_body, send)

    async def _send_too_large(self, scope: Any, receive: Any, send: Any) -> None:
        response = JSONResponse(
            status_code=413,
            content=_failure_payload(
                code="request_too_large",
                message="The search request exceeds the configured byte limit.",
                action="Send only the bounded v1 search fields.",
            ),
        )
        await response(scope, receive, send)


def _failure_payload(
    *,
    code: str,
    message: str,
    action: str,
    details: list[ErrorDetail] | None = None,
) -> dict[str, Any]:
    response = SearchResponse(
        status=SearchStatus.FAILED,
        jobs=[],
        error=ErrorEnvelope(
            code=code,
            message=message,
            action=action,
            details=details,
        ),
        provider_version=provider_version(),
    )
    return response.model_dump(mode="json", by_alias=True, exclude_none=True)


def create_app(
    backend: JobSearchBackend | None = None,
    settings: Settings | None = None,
) -> FastAPI:
    resolved_settings = settings or Settings.from_environment()
    resolved_backend = backend or PythonJobSpyBackend(
        blocked_retry_after_seconds=resolved_settings.blocked_retry_after_seconds,
        transient_retry_after_seconds=resolved_settings.transient_retry_after_seconds,
    )
    resolved_provider_version = provider_version()
    service = SearchService(
        resolved_backend,
        resolved_settings,
        resolved_provider_version,
    )

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        yield
        service.close()

    application = FastAPI(
        title="Job Hunter JobSpy API",
        version=SERVICE_VERSION,
        docs_url=None,
        redoc_url=None,
        openapi_url=None,
        lifespan=lifespan,
    )
    application.add_middleware(
        RequestBodyLimitMiddleware,
        maximum_bytes=resolved_settings.max_request_bytes,
    )

    @application.exception_handler(RequestValidationError)
    async def validation_error_handler(
        _: Request,
        error: RequestValidationError,
    ) -> JSONResponse:
        details: list[ErrorDetail] = []
        for item in error.errors()[:20]:
            location = item.get("loc", ())
            field_parts = [
                str(part)
                for part in location
                if part not in {"body", "query", "path"}
            ]
            details.append(
                ErrorDetail(
                    field=".".join(field_parts) or "$",
                    message=str(item.get("msg", "Invalid value."))[:256],
                    code=str(item.get("type", "validation_error"))[:128],
                )
            )
        return JSONResponse(
            status_code=422,
            content=_failure_payload(
                code="invalid_request",
                message="The search request is invalid.",
                action=(
                    "Use only source, searchTerm, location, resultsWanted, and "
                    "hoursOld within their documented bounds."
                ),
                details=details,
            ),
        )

    @application.get(
        "/health",
        response_model=HealthResponse,
        response_model_by_alias=True,
    )
    async def health() -> HealthResponse:
        return HealthResponse()

    @application.get(
        "/version",
        response_model=VersionResponse,
        response_model_by_alias=True,
    )
    async def version() -> VersionResponse:
        return VersionResponse(
            service_version=SERVICE_VERSION,
            contract_version=CONTRACT_VERSION,
            provider_version=resolved_provider_version,
        )

    @application.post(
        "/v1/search",
        response_model=SearchResponse,
        response_model_by_alias=True,
        response_model_exclude_none=True,
    )
    async def search(request: SearchRequest) -> SearchResponse:
        return await service.search(request)

    return application


app = create_app()

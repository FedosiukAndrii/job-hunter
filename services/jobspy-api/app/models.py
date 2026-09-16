from __future__ import annotations

from datetime import datetime
from enum import Enum
from typing import Annotated, Literal

from pydantic import (
    AliasChoices,
    BaseModel,
    ConfigDict,
    Field,
    StringConstraints,
    field_validator,
)

MAX_QUERY_LENGTH = 256
MAX_LOCATION_LENGTH = 256
MAX_RESULTS_WANTED = 50
MAX_HOURS_OLD = 8_760


def _to_camel(value: str) -> str:
    head, *tail = value.split("_")
    return head + "".join(part.capitalize() for part in tail)


class ContractModel(BaseModel):
    model_config = ConfigDict(
        alias_generator=_to_camel,
        extra="forbid",
        populate_by_name=True,
    )


class RequestContractModel(BaseModel):
    model_config = ConfigDict(
        alias_generator=_to_camel,
        extra="forbid",
        populate_by_name=False,
    )


class SourceName(str, Enum):
    LINKEDIN = "linkedin"


class SearchStatus(str, Enum):
    SUCCEEDED = "succeeded"
    PARTIAL = "partial"
    BLOCKED = "blocked"
    FAILED = "failed"


BoundedQuery = Annotated[
    str,
    StringConstraints(
        strip_whitespace=True,
        min_length=1,
        max_length=MAX_QUERY_LENGTH,
    ),
]
BoundedLocation = Annotated[
    str,
    StringConstraints(
        strip_whitespace=True,
        min_length=1,
        max_length=MAX_LOCATION_LENGTH,
    ),
]


class SearchRequest(RequestContractModel):
    source: SourceName
    search_term: BoundedQuery = Field(
        validation_alias=AliasChoices("searchTerm", "query"),
        serialization_alias="searchTerm",
        description="The bounded job-search query.",
    )
    location: BoundedLocation | None = None
    results_wanted: int = Field(
        default=20,
        ge=1,
        le=MAX_RESULTS_WANTED,
        strict=True,
    )
    hours_old: int = Field(
        default=168,
        ge=1,
        le=MAX_HOURS_OLD,
        strict=True,
    )

    @field_validator("search_term", "location")
    @classmethod
    def normalize_search_text(cls, value: str | None) -> str | None:
        if value is None:
            return None
        if any(
            not character.isprintable() and not character.isspace()
            for character in value
        ):
            raise ValueError("must not contain control characters")
        normalized = " ".join(value.split())
        if not normalized:
            raise ValueError("must not be blank")
        return normalized


class Job(ContractModel):
    source: Literal["linkedin"]
    source_job_id: str
    source_url: str
    title: str
    company: str
    description: str
    location: str | None = None
    workplace_mode: str | None = None
    employment_type: str | None = None
    seniority: str | None = None
    skills: list[str] | None = None
    categories: list[str] | None = None
    compensation_minimum: float | None = None
    compensation_maximum: float | None = None
    compensation_currency: str | None = None
    compensation_period: str | None = None
    published_at_utc: datetime | None = None
    application_url: str | None = None


class ErrorDetail(ContractModel):
    field: str
    message: str
    code: str


class ErrorEnvelope(ContractModel):
    code: str
    message: str
    retry_after_seconds: int | None = None
    action: str | None = None
    details: list[ErrorDetail] | None = None


class SearchResponse(ContractModel):
    status: SearchStatus
    jobs: list[Job] = Field(default_factory=list)
    error: ErrorEnvelope | None = None
    provider_version: str


class HealthResponse(ContractModel):
    status: Literal["ok"] = "ok"
    allowed_sources: list[Literal["linkedin"]] = Field(
        default_factory=lambda: ["linkedin"]
    )
    source_concurrency: dict[str, int] = Field(
        default_factory=lambda: {"linkedin": 1}
    )


class VersionResponse(ContractModel):
    service_version: str
    contract_version: Literal["v1"] = "v1"
    provider_name: Literal["python-jobspy"] = "python-jobspy"
    provider_version: str

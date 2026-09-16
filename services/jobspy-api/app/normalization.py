from __future__ import annotations

import math
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from datetime import date, datetime, time, timezone
from numbers import Real
from typing import Any
from urllib.parse import urlsplit, urlunsplit

from app.models import Job

_MAX_DESCRIPTION_BYTES = 100_000
_MAX_LIST_ITEMS = 32


class MalformedRecordError(ValueError):
    pass


@dataclass(frozen=True, slots=True)
class NormalizationResult:
    jobs: list[Job]
    invalid_count: int
    truncated_count: int


def normalize_records(
    records: Sequence[Mapping[str, Any]],
    maximum_results: int,
) -> NormalizationResult:
    jobs: list[Job] = []
    invalid_count = 0
    truncated_count = 0
    seen_ids: set[str] = set()

    for index, raw_record in enumerate(records):
        if index >= maximum_results:
            truncated_count += 1
            continue
        if not isinstance(raw_record, Mapping):
            invalid_count += 1
            continue
        try:
            job = normalize_record(raw_record)
        except (MalformedRecordError, TypeError, ValueError, OverflowError):
            invalid_count += 1
            continue
        if job.source_job_id in seen_ids:
            invalid_count += 1
            continue
        seen_ids.add(job.source_job_id)
        jobs.append(job)

    return NormalizationResult(
        jobs=jobs,
        invalid_count=invalid_count,
        truncated_count=truncated_count,
    )


def normalize_record(raw: Mapping[str, Any]) -> Job:
    source = _required_text(_pick(raw, "site", "source"), 32).lower()
    if source != "linkedin":
        raise MalformedRecordError("unsupported backend source")

    source_job_id = _required_identifier(
        _pick(raw, "id", "sourceJobId", "source_job_id")
    )
    source_url = _safe_url(
        _pick(raw, "job_url", "sourceUrl", "source_url"),
        linkedin_only=True,
        required=True,
    )
    title = _required_text(_pick(raw, "title"), 512)
    company = _required_text(
        _pick(raw, "company", "company_name", "companyName"),
        512,
    )
    description = _required_text(_pick(raw, "description"), _MAX_DESCRIPTION_BYTES)

    workplace_mode = _workplace_mode(raw)
    employment_type = _employment_type(
        _pick(raw, "job_type", "jobType", "employmentType", "employment_type")
    )
    compensation_minimum = _number(
        _pick(raw, "min_amount", "compensationMinimum", "compensation_minimum")
    )
    compensation_maximum = _number(
        _pick(raw, "max_amount", "compensationMaximum", "compensation_maximum")
    )

    return Job(
        source="linkedin",
        source_job_id=source_job_id,
        source_url=source_url,
        title=title,
        company=company,
        description=description,
        location=_optional_text(_pick(raw, "location"), 512),
        workplace_mode=workplace_mode,
        employment_type=employment_type,
        seniority=_optional_text(
            _pick(raw, "job_level", "jobLevel", "seniority"),
            128,
        ),
        skills=_string_list(_pick(raw, "skills")),
        categories=_categories(raw),
        compensation_minimum=compensation_minimum,
        compensation_maximum=compensation_maximum,
        compensation_currency=_currency(
            _pick(raw, "currency", "compensationCurrency", "compensation_currency")
        ),
        compensation_period=_compensation_period(
            _pick(raw, "interval", "compensationPeriod", "compensation_period")
        ),
        published_at_utc=_published_at(
            _pick(raw, "date_posted", "publishedAtUtc", "published_at_utc")
        ),
        application_url=_safe_url(
            _pick(raw, "job_url_direct", "applicationUrl", "application_url"),
            linkedin_only=False,
            required=False,
        ),
    )


def _pick(raw: Mapping[str, Any], *keys: str) -> Any:
    for key in keys:
        if key in raw and not _is_missing(raw[key]):
            return raw[key]
    return None


def _is_missing(value: Any) -> bool:
    if value is None:
        return True
    if isinstance(value, Real) and not isinstance(value, bool):
        try:
            return not math.isfinite(float(value))
        except (TypeError, ValueError, OverflowError):
            return True
    return type(value).__name__ in {"NAType", "NaTType"}


def _clean_text(value: Any) -> str:
    if not isinstance(value, str):
        raise MalformedRecordError("text field is not a string")
    without_controls = "".join(
        character if character.isprintable() or character.isspace() else " "
        for character in value
    )
    return " ".join(without_controls.split())


def _truncate_utf8(value: str, maximum_bytes: int) -> str:
    encoded = value.encode("utf-8")
    if len(encoded) <= maximum_bytes:
        return value
    return encoded[:maximum_bytes].decode("utf-8", errors="ignore").rstrip()


def _required_text(value: Any, maximum_bytes: int) -> str:
    if _is_missing(value):
        raise MalformedRecordError("required text is missing")
    text = _truncate_utf8(_clean_text(value), maximum_bytes)
    if not text:
        raise MalformedRecordError("required text is blank")
    return text


def _optional_text(value: Any, maximum_bytes: int) -> str | None:
    if _is_missing(value):
        return None
    text = _truncate_utf8(_clean_text(value), maximum_bytes)
    return text or None


def _required_identifier(value: Any) -> str:
    if isinstance(value, bool) or _is_missing(value):
        raise MalformedRecordError("source identifier is missing")
    if isinstance(value, (str, int)):
        identifier = str(value).strip()
    else:
        raise MalformedRecordError("source identifier has an invalid type")
    if not identifier or len(identifier) > 256:
        raise MalformedRecordError("source identifier has an invalid length")
    if any(not character.isprintable() for character in identifier):
        raise MalformedRecordError("source identifier contains control characters")
    return identifier


def _safe_url(
    value: Any,
    *,
    linkedin_only: bool,
    required: bool,
) -> str | None:
    if _is_missing(value):
        if required:
            raise MalformedRecordError("required URL is missing")
        return None
    if not isinstance(value, str) or len(value) > 4_096:
        raise MalformedRecordError("URL has an invalid type or length")
    normalized_value = value.strip()
    if not normalized_value and not required:
        return None
    parts = urlsplit(normalized_value)
    if (
        parts.scheme.lower() != "https"
        or not parts.hostname
        or parts.username is not None
        or parts.password is not None
    ):
        raise MalformedRecordError("URL must be credential-free HTTPS")
    try:
        port = parts.port
    except ValueError as error:
        raise MalformedRecordError("URL port is invalid") from error
    if port not in (None, 443):
        raise MalformedRecordError("URL port is not allowed")
    host = parts.hostname.lower().rstrip(".")
    if linkedin_only and host != "linkedin.com" and not host.endswith(".linkedin.com"):
        raise MalformedRecordError("source URL must use LinkedIn")
    return urlunsplit((parts.scheme.lower(), parts.netloc, parts.path, parts.query, ""))


def _enum_text(value: Any) -> str | None:
    if _is_missing(value):
        return None
    enum_value = getattr(value, "value", value)
    if isinstance(enum_value, tuple):
        enum_value = enum_value[0] if enum_value else None
    return _optional_text(enum_value, 128)


def _employment_type(value: Any) -> str | None:
    if isinstance(value, Sequence) and not isinstance(value, (str, bytes)):
        value = next(iter(value), None)
    raw_value = _enum_text(value)
    if raw_value is None:
        return None
    normalized = raw_value.lower().replace("-", "").replace("_", "").replace(" ", "")
    return {
        "fulltime": "full_time",
        "parttime": "part_time",
        "contract": "contract",
        "contractor": "contract",
        "temporary": "temporary",
        "internship": "internship",
    }.get(normalized)


def _workplace_mode(raw: Mapping[str, Any]) -> str | None:
    explicit = _enum_text(
        _pick(raw, "workplaceMode", "workplace_mode", "work_from_home_type")
    )
    if explicit:
        normalized = explicit.lower().replace("-", "_").replace(" ", "_")
        if normalized in {"remote", "hybrid"}:
            return normalized
        if normalized in {"onsite", "on_site"}:
            return "on_site"
    is_remote = _pick(raw, "is_remote", "isRemote")
    return "remote" if is_remote is True else None


def _string_list(value: Any) -> list[str] | None:
    if _is_missing(value):
        return None
    if isinstance(value, str):
        candidates: Sequence[Any] = value.split(",")
    elif isinstance(value, Sequence) and not isinstance(value, (bytes, bytearray)):
        candidates = value
    else:
        raise MalformedRecordError("list field has an invalid type")
    result: list[str] = []
    seen: set[str] = set()
    for candidate in candidates:
        text = _optional_text(candidate, 128)
        if text and text.casefold() not in seen:
            seen.add(text.casefold())
            result.append(text)
        if len(result) >= _MAX_LIST_ITEMS:
            break
    return result or None


def _categories(raw: Mapping[str, Any]) -> list[str] | None:
    explicit = _pick(raw, "categories")
    if explicit is not None:
        return _string_list(explicit)
    values = [
        value
        for value in (
            _pick(raw, "job_function", "jobFunction"),
            _pick(raw, "company_industry", "companyIndustry"),
        )
        if value is not None
    ]
    return _string_list(values) if values else None


def _number(value: Any) -> float | None:
    if _is_missing(value):
        return None
    if isinstance(value, bool):
        raise MalformedRecordError("numeric field has an invalid type")
    try:
        number = float(value)
    except (TypeError, ValueError, OverflowError) as error:
        raise MalformedRecordError("numeric field is invalid") from error
    if not math.isfinite(number) or number < 0:
        raise MalformedRecordError("numeric field is outside the allowed range")
    return number


def _currency(value: Any) -> str | None:
    if _is_missing(value):
        return None
    currency = _clean_text(value)
    if len(currency) != 3 or not currency.isascii() or not currency.isalpha():
        return None
    return currency.upper()


def _compensation_period(value: Any) -> str | None:
    raw_value = _enum_text(value)
    if raw_value is None:
        return None
    return {
        "hour": "hour",
        "hourly": "hour",
        "month": "month",
        "monthly": "month",
        "year": "year",
        "yearly": "year",
    }.get(raw_value.lower())


def _published_at(value: Any) -> datetime | None:
    if _is_missing(value):
        return None
    if isinstance(value, datetime):
        parsed = value
    elif isinstance(value, date):
        parsed = datetime.combine(value, time.min, tzinfo=timezone.utc)
    elif isinstance(value, str):
        candidate = value.strip()
        if candidate.endswith("Z"):
            candidate = candidate[:-1] + "+00:00"
        try:
            parsed = datetime.fromisoformat(candidate)
        except ValueError as error:
            raise MalformedRecordError("published date is invalid") from error
    else:
        raise MalformedRecordError("published date has an invalid type")
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)

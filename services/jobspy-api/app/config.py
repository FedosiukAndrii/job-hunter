from __future__ import annotations

import os
from dataclasses import dataclass

SERVICE_VERSION = "0.1.0"
CONTRACT_VERSION = "v1"


def _read_int(
    name: str,
    default: int,
    minimum: int,
    maximum: int,
) -> int:
    raw_value = os.getenv(name)
    if raw_value is None:
        return default
    try:
        value = int(raw_value)
    except ValueError as error:
        raise ValueError(f"{name} must be an integer") from error
    if value < minimum or value > maximum:
        raise ValueError(f"{name} must be between {minimum} and {maximum}")
    return value


@dataclass(frozen=True, slots=True)
class Settings:
    linkedin_timeout_seconds: float = 40
    blocked_retry_after_seconds: int = 86_400
    transient_retry_after_seconds: int = 900
    malformed_retry_after_seconds: int = 3_600
    max_request_bytes: int = 16_384
    max_response_bytes: int = 1_900_000

    def __post_init__(self) -> None:
        if self.linkedin_timeout_seconds <= 0 or self.linkedin_timeout_seconds > 300:
            raise ValueError("linkedin_timeout_seconds must be in (0, 300]")
        for name, value in (
            ("blocked_retry_after_seconds", self.blocked_retry_after_seconds),
            ("transient_retry_after_seconds", self.transient_retry_after_seconds),
            ("malformed_retry_after_seconds", self.malformed_retry_after_seconds),
        ):
            if value <= 0 or value > 604_800:
                raise ValueError(f"{name} must be in (0, 604800]")
        if self.max_request_bytes < 1_024 or self.max_request_bytes > 1_048_576:
            raise ValueError("max_request_bytes must be between 1024 and 1048576")
        if self.max_response_bytes < 16_384 or self.max_response_bytes > 2_000_000:
            raise ValueError("max_response_bytes must be between 16384 and 2000000")

    @classmethod
    def from_environment(cls) -> Settings:
        return cls(
            linkedin_timeout_seconds=_read_int(
                "JOBSPY_LINKEDIN_TIMEOUT_SECONDS",
                40,
                1,
                300,
            ),
            blocked_retry_after_seconds=_read_int(
                "JOBSPY_BLOCKED_RETRY_SECONDS",
                86_400,
                3_600,
                604_800,
            ),
            transient_retry_after_seconds=_read_int(
                "JOBSPY_TRANSIENT_RETRY_SECONDS",
                900,
                60,
                86_400,
            ),
            malformed_retry_after_seconds=_read_int(
                "JOBSPY_MALFORMED_RETRY_SECONDS",
                3_600,
                300,
                604_800,
            ),
            max_request_bytes=_read_int(
                "JOBSPY_MAX_REQUEST_BYTES",
                16_384,
                1_024,
                1_048_576,
            ),
            max_response_bytes=_read_int(
                "JOBSPY_MAX_RESPONSE_BYTES",
                1_900_000,
                16_384,
                2_000_000,
            ),
        )

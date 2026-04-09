from __future__ import annotations

import gzip


def decode_artifact_bytes(payload: bytes) -> bytes:
    if payload.startswith(b"\x1f\x8b"):
        return gzip.decompress(payload)
    return payload

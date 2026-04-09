from __future__ import annotations

import gzip

from analytics_sync.extract.artifact_decoder import decode_artifact_bytes


def test_decode_artifact_bytes_returns_plain_json_unchanged():
    payload = b'{"status":"ok"}'

    decoded = decode_artifact_bytes(payload)

    assert decoded == payload


def test_decode_artifact_bytes_decompresses_gzip_payload():
    payload = gzip.compress(b'{"status":"ok"}')

    decoded = decode_artifact_bytes(payload)

    assert decoded == b'{"status":"ok"}'

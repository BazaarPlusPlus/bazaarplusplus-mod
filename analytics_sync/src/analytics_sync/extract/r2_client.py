from __future__ import annotations

from dataclasses import dataclass

import boto3  # type: ignore[import-untyped]
from botocore.client import BaseClient  # type: ignore[import-untyped]


@dataclass(frozen=True)
class R2ObjectRef:
    bucket: str
    key: str


class R2Client:
    def __init__(self, client: BaseClient) -> None:
        self._client = client

    @classmethod
    def create(
        cls,
        *,
        account_id: str,
        access_key_id: str,
        secret_access_key: str,
    ) -> "R2Client":
        client = boto3.client(
            "s3",
            endpoint_url=f"https://{account_id}.r2.cloudflarestorage.com",
            aws_access_key_id=access_key_id,
            aws_secret_access_key=secret_access_key,
        )
        return cls(client)

    def get_bytes(self, ref: R2ObjectRef) -> bytes:
        response = self._client.get_object(Bucket=ref.bucket, Key=ref.key)
        return bytes(response["Body"].read())

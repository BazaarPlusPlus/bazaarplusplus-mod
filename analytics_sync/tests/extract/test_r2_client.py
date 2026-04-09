from analytics_sync.extract.r2_client import R2ObjectRef


def test_object_ref_keeps_bucket_and_key():
    ref = R2ObjectRef(bucket="pvp-bucket", key="runs/a/b.json")

    assert ref.bucket == "pvp-bucket"
    assert ref.key == "runs/a/b.json"

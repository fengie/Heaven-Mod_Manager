import sys
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from heaven_security import (
    PermissionBroker,
    SecretHandleDirectory,
    SecretHandleError,
    SecretHandleResolver,
    validate_capability_permissions,
)


class SecurityTests(unittest.TestCase):
    def test_permission_broker_allows_exact_and_wildcard_but_denies_missing(self):
        broker = PermissionBroker()
        exact = broker.authorize(
            capability="filesystem.write",
            required_permission="filesystem.write",
            resource="path:C:/repo/a.txt",
            mutation=True,
            granted_permissions=["filesystem.write"],
        )
        self.assertTrue(exact.allowed)

        wildcard = broker.authorize(
            capability="git.status",
            required_permission="repository.read",
            resource="repo:C:/repo",
            mutation=False,
            granted_permissions=["repository.*"],
        )
        self.assertTrue(wildcard.allowed)

        denied = broker.authorize(
            capability="filesystem.write",
            required_permission="filesystem.write",
            resource="path:C:/repo/a.txt",
            mutation=True,
            granted_permissions=["filesystem.read"],
        )
        self.assertFalse(denied.allowed)
        self.assertIn("filesystem.write", denied.reason)

    def test_secret_directory_rejects_unknown_expired_and_wrong_purpose(self):
        now = time.time()
        directory = SecretHandleDirectory()
        directory.register(
            "opaque:release",
            "RELEASE_TOKEN_REF",
            purposes=["release.publish"],
            expires_at=now + 60,
        )
        resolver = SecretHandleResolver(directory, now_fn=lambda: now)

        binding = resolver.resolve_secret(
            "opaque:release",
            "release.publish",
            30,
        )
        self.assertEqual(binding.transport_handle, "RELEASE_TOKEN_REF")
        self.assertNotIn("opaque:release", repr(binding))
        self.assertNotIn("transport_handle", binding.as_dict())

        with self.assertRaises(SecretHandleError) as unknown:
            resolver.resolve_secret("opaque:missing", "release.publish", 30)
        self.assertEqual(unknown.exception.code, "SECRET_HANDLE_UNKNOWN")

        with self.assertRaises(SecretHandleError) as purpose:
            resolver.resolve_secret("opaque:release", "browser.login", 30)
        self.assertEqual(purpose.exception.code, "SECRET_PURPOSE_DENIED")

        expired = SecretHandleDirectory()
        expired._entries["opaque:expired"] = {
            "transport_handle": "EXPIRED_REF",
            "purposes": frozenset({"release.publish"}),
            "expires_at": now - 1,
        }
        expired_resolver = SecretHandleResolver(expired, now_fn=lambda: now)
        with self.assertRaises(SecretHandleError) as expiry:
            expired_resolver.resolve_secret("opaque:expired", "release.publish", 30)
        self.assertEqual(expiry.exception.code, "SECRET_HANDLE_EXPIRED")

    def test_resolver_enforces_ttl_limit(self):
        now = time.time()
        directory = SecretHandleDirectory()
        directory.register(
            "opaque:short",
            "SHORT_REF",
            purposes=["execution.run"],
            expires_at=now + 60,
        )
        resolver = SecretHandleResolver(
            directory,
            max_ttl_seconds=30,
            now_fn=lambda: now,
        )
        with self.assertRaises(SecretHandleError) as ctx:
            resolver.resolve_secret("opaque:short", "execution.run", 31)
        self.assertEqual(ctx.exception.code, "SECRET_TTL_INVALID")

    def test_manifest_permission_validation_is_exact(self):
        validate_capability_permissions(
            {"git.status": "repository.read"},
            {"git.status": "repository.read"},
        )
        with self.assertRaises(ValueError):
            validate_capability_permissions(
                {"git.status": "repository.write"},
                {"git.status": "repository.read"},
            )


if __name__ == "__main__":
    unittest.main()

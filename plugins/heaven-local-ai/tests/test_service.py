import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from heaven_local_ai import HeavenLocalAiPlugin


class FakeControlPlane:
    def __init__(self):
        self.calls = []

    def invoke(self, capability_name, payload=None, **kwargs):
        self.calls.append((capability_name, dict(payload or {})))
        return {"status": "ok", "data": {"stdout": "ok"}}


class LocalAiTests(unittest.TestCase):
    def test_generate_uses_bounded_execution_and_base64_prompt(self):
        cp = FakeControlPlane()
        plugin = HeavenLocalAiPlugin(cp)
        plugin.generate("qwen2.5-coder:7b", "hello'; Remove-Item C:\\ -Recurse", timeout_seconds=10)
        capability, payload = cp.calls[0]
        self.assertEqual(capability, "execution.run")
        self.assertNotIn("Remove-Item", payload["command"])
        self.assertIn("FromBase64String", payload["command"])
        self.assertEqual(payload["timeout_seconds"], 10)

    def test_model_name_blocks_shell_metacharacters(self):
        cp = FakeControlPlane()
        plugin = HeavenLocalAiPlugin(cp)
        with self.assertRaises(ValueError):
            plugin.generate("qwen;whoami", "hello")

    def test_prompt_is_bounded(self):
        cp = FakeControlPlane()
        plugin = HeavenLocalAiPlugin(cp)
        with self.assertRaises(ValueError):
            plugin.generate("qwen2.5", "x" * 50001)

    def test_models_is_fixed_command(self):
        cp = FakeControlPlane()
        HeavenLocalAiPlugin(cp).models()
        self.assertEqual(cp.calls[0][0], "execution.run")
        self.assertEqual(cp.calls[0][1]["timeout_seconds"], 30)


if __name__ == "__main__":
    unittest.main()

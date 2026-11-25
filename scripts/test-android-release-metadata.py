import importlib.util
from pathlib import Path
import unittest


spec = importlib.util.spec_from_file_location("metadata", Path(__file__).with_name("android-release-metadata.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReleaseMetadataTests(unittest.TestCase):
    def test_preview_and_stable_are_distinct(self):
        preview = module.metadata("push", "refs/heads/develop", "true", "794")
        stable = module.metadata("push", "refs/heads/main", "true", "795")
        self.assertEqual(preview, dict(display="0.1.794-preview.794", code="794",
                                       tag="android-v0.1.794-preview.794", channel="preview"))
        self.assertEqual(stable, dict(display="0.1.795", code="795", tag="android-v0.1.795", channel="stable"))
        self.assertGreater(int(stable["code"]), int(preview["code"]))

    def test_pull_requests_never_publish_even_with_a_protected_base(self):
        for event in ("pull_request", "workflow_dispatch"):
            self.assertEqual(module.metadata(event, "refs/heads/main", "true", "794")["channel"], "none")

    def test_unprotected_pushes_fail(self):
        for ref in ("refs/heads/main", "refs/heads/develop", "refs/tags/v0.1.0-android-preview.1"):
            with self.subTest(ref=ref), self.assertRaises(ValueError):
                module.metadata("push", ref, "false", "794")

    def test_other_branches_and_tags_fail(self):
        for ref in ("refs/heads/master", "refs/heads/preview", "refs/heads/feature", "refs/tags/v1.0.0"):
            with self.subTest(ref=ref), self.assertRaises(ValueError):
                module.metadata("push", ref, "true", "794")

    def test_legacy_preview_tags(self):
        result = module.metadata("push", "refs/tags/v0.1.0-android-preview.21", "true", "794")
        self.assertEqual(result["display"], "0.1.0")
        self.assertEqual(result["channel"], "preview")
        self.assertEqual(result["tag"], "v0.1.0-android-preview.21")

    def test_android_code_limits(self):
        for code in ("0", "-1", "2100000001", "invalid"):
            with self.subTest(code=code), self.assertRaises(ValueError):
                module.metadata("push", "refs/heads/main", "true", code)
        self.assertEqual(module.metadata("push", "refs/heads/main", "true", "2100000000")["code"], "2100000000")

    def test_workflow_keeps_signing_and_publishing_off_pull_requests(self):
        workflow = (Path(__file__).parent.parent / ".github/workflows/android-preview.yml").read_text()
        gate = "if: github.event_name == 'push' && github.ref_protected &&"
        self.assertEqual(workflow.count(gate), 2)
        self.assertIn("environment: android-preview", workflow)
        self.assertIn("flags=(--latest=false)", workflow)
        self.assertIn("flags+=(--target \"$GITHUB_SHA\")", workflow)
        self.assertIn("if: github.event_name == 'pull_request'", workflow)
        self.assertNotIn("pull_request_target", workflow)



if __name__ == "__main__":
    unittest.main()

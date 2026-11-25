import importlib.util
from pathlib import Path
import unittest


spec = importlib.util.spec_from_file_location("notes", Path(__file__).with_name("android-release-notes.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

RELEASES = [
    dict(tag_name="android-v0.1.813", prerelease=False, created_at="2026-11-25T15:27:35Z"),
    dict(tag_name="android-v0.1.806-preview.806", prerelease=True, created_at="2026-11-25T15:00:09Z"),
    dict(tag_name="v0.1.0-android-preview.21", prerelease=True, created_at="2026-11-25T12:32:27Z"),
    dict(tag_name="v0.1.0-android-preview.20", prerelease=True, created_at="2026-11-25T01:16:03Z"),
    dict(tag_name="v2.0.0", prerelease=False, created_at="2026-11-25T16:00:00Z"),
    dict(tag_name="android-v0.1.900-preview.900", prerelease=True, draft=True, created_at="2026-11-25T17:00:00Z"),
]
GENERATED = """## What's Changed
* Rich Android notifications by @someone in https://github.com/Forrest-Technologies/drover/pull/954
* Release notes for APKs by @someone in https://github.com/Forrest-Technologies/drover/pull/956

**Full Changelog**: https://github.com/Forrest-Technologies/drover/compare/a...b"""


class ReleaseNotesTests(unittest.TestCase):
    def test_preview_and_stable_diff_against_their_own_channel(self):
        self.assertEqual(module.previous_tag(RELEASES, "preview", "android-v0.1.820-preview.820"),
                         "android-v0.1.806-preview.806")
        self.assertEqual(module.previous_tag(RELEASES, "stable", "android-v0.1.830"), "android-v0.1.813")

    def test_current_draft_and_unrelated_releases_are_skipped(self):
        self.assertEqual(module.previous_tag(RELEASES, "preview", "android-v0.1.806-preview.806"),
                         "v0.1.0-android-preview.21")
        self.assertEqual(module.previous_tag(RELEASES, "stable", "android-v0.1.813"), "")

    def test_unknown_channel_fails(self):
        with self.assertRaises(ValueError):
            module.previous_tag(RELEASES, "none", "android-v0.1.1")

    def test_body_counts_pull_requests_and_keeps_install_line(self):
        text = module.body(GENERATED, "preview", "android-v0.1.806-preview.806")
        self.assertTrue(text.startswith("2 merged pull requests since android-v0.1.806-preview.806"))
        self.assertIn("pull/954", text)
        self.assertTrue(text.rstrip().endswith(module.INSTALL))

    def test_body_without_previous_or_changes(self):
        self.assertTrue(module.body("", "stable", "").startswith("First stable release in this series."))
        self.assertTrue(module.body("## What's Changed\n", "preview", "android-v0.1.806-preview.806")
                        .startswith("No merged pull requests since android-v0.1.806-preview.806."))

    def test_workflow_publishes_generated_notes(self):
        workflow = (Path(__file__).parent.parent / ".github/workflows/android-preview.yml").read_text()
        self.assertIn("scripts/android-release-notes.py previous", workflow)
        self.assertIn("releases/generate-notes", workflow)
        self.assertIn("--notes-file", workflow)
        self.assertNotIn("--notes '", workflow)


if __name__ == "__main__":
    unittest.main()

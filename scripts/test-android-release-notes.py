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
GENERATED_WITH_CONTRIBUTORS = """## What's Changed
* Rich Android notifications by @someone in https://github.com/Forrest-Technologies/drover/pull/954
* Release notes for APKs by @firsttimer in https://github.com/Forrest-Technologies/drover/pull/956

**Full Changelog**: https://github.com/Forrest-Technologies/drover/compare/a...b

## New Contributors
* @firsttimer made their first contribution in https://github.com/Forrest-Technologies/drover/pull/956"""


class ReleaseNotesTests(unittest.TestCase):
    def test_preview_and_stable_diff_against_their_own_channel(self):
        self.assertEqual(module.previous_tag(RELEASES, "preview", "android-v0.1.820-preview.820"),
                         "android-v0.1.806-preview.806")
        self.assertEqual(module.previous_tag(RELEASES, "stable", "android-v0.1.830"), "android-v0.1.813")

    def test_current_draft_and_unrelated_releases_are_skipped(self):
        self.assertEqual(module.previous_tag(RELEASES, "preview", "android-v0.1.806-preview.806"),
                         "v0.1.0-android-preview.21")
        self.assertEqual(module.previous_tag(RELEASES, "stable", "android-v0.1.813"), "")

    def test_paginated_release_pages_are_flattened(self):
        pages = [[RELEASES[0]], RELEASES[1:]]
        self.assertEqual(module.previous_tag(pages, "stable", "android-v0.1.830"), "android-v0.1.813")
        self.assertEqual(module.previous_tag(pages, "preview", "android-v0.1.820-preview.820"),
                         "android-v0.1.806-preview.806")

    def test_missing_tag_and_published_at_order(self):
        releases = [
            dict(tag_name=None, created_at="2026-11-25T19:00:00Z"),
            dict(created_at="2026-11-25T19:00:00Z"),
            dict(tag_name="android-v0.1.814", created_at="2026-11-25T18:00:00Z",
                 published_at="2026-11-20T00:00:00Z"),
            dict(tag_name="android-v0.1.813", created_at="2026-11-25T15:27:35Z",
                 published_at="2026-11-25T15:27:35Z"),
        ]
        self.assertEqual(module.previous_tag(releases, "stable", "android-v0.1.830"), "android-v0.1.813")
        self.assertEqual(module.previous_tags(releases, "stable", "android-v0.1.830"),
                         ["android-v0.1.813", "android-v0.1.814"])

    def test_unknown_channel_fails(self):
        with self.assertRaises(ValueError):
            module.previous_tag(RELEASES, "none", "android-v0.1.1")

    def test_body_counts_distinct_pull_requests_and_keeps_install_line(self):
        text = module.body(GENERATED, "preview", "android-v0.1.806-preview.806")
        self.assertTrue(text.startswith("2 merged pull requests since android-v0.1.806-preview.806"))
        self.assertIn("pull/954", text)
        self.assertTrue(text.rstrip().endswith(module.INSTALL))

    def test_new_contributors_section_does_not_double_count(self):
        text = module.body(GENERATED_WITH_CONTRIBUTORS, "preview", "android-v0.1.806-preview.806")
        self.assertTrue(text.startswith("2 merged pull requests since android-v0.1.806-preview.806"))
        self.assertIn("## New Contributors", text)

    def test_body_uses_singular_wording_for_one_pull_request(self):
        generated = "* One change by @someone in https://github.com/Forrest-Technologies/drover/pull/1"
        text = module.body(generated, "stable", "android-v0.1.813")
        self.assertTrue(text.startswith("1 merged pull request since android-v0.1.813,"))
        self.assertNotIn("1 merged pull requests", text)

    def test_body_without_previous_or_changes(self):
        self.assertTrue(module.body("", "stable", "").startswith("First stable release in this series."))
        self.assertTrue(module.body("## What's Changed\n", "preview", "android-v0.1.806-preview.806")
                        .startswith("No merged pull requests since android-v0.1.806-preview.806."))

    def test_fallback_has_install_line_and_no_false_summary(self):
        text = module.fallback()
        self.assertEqual(text, module.INSTALL + "\n")
        self.assertNotIn("First ", text)
        self.assertNotIn("No merged", text)

    def test_workflow_publishes_generated_notes(self):
        workflow = (Path(__file__).parent.parent / ".github/workflows/android-preview.yml").read_text()
        self.assertIn("scripts/android-release-notes.py previous", workflow)
        self.assertIn("scripts/android-release-notes.py previous-tags", workflow)
        self.assertIn("gh api --paginate --slurp", workflow)
        self.assertIn("releases/generate-notes", workflow)
        self.assertIn("previous_tag_name=", workflow)
        self.assertIn("scripts/android-release-notes.py fallback", workflow)
        self.assertIn("::warning::", workflow)
        self.assertIn("--notes-file", workflow)
        self.assertNotIn("--notes '", workflow)


if __name__ == "__main__":
    unittest.main()

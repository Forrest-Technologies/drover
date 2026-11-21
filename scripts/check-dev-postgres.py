"""Check binary discovery without starting a database or requiring Homebrew."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).with_name("dev-postgres.sh").read_text(encoding="utf-8")
DISCOVERY = SCRIPT.split("# PostgreSQL refuses to run as root", 1)[0]


class BinaryDiscoveryTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="postgres discovery ")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.tools = self.root / "tools"
        self.tools.mkdir()
        self.environment = dict(os.environ, PATH=f"{self.tools}:/usr/bin:/bin")
        self.environment["CHECK_KEGS"] = str(self.root)
        self.environment["CHECK_FORMULAE"] = ""
        self.environment["CHECK_BREW_FAILURE"] = "0"
        self.executable(self.tools / "brew", '''#!/bin/bash
case "$1" in
  list)
    [ "$CHECK_BREW_FAILURE" = 0 ] || exit 1
    printf '%s\\n' "$CHECK_FORMULAE"
    ;;
  --prefix) printf '%s/%s\\n' "$CHECK_KEGS" "$2" ;;
  *) exit 1 ;;
esac
''')
        # Keep the Linux fallback deterministic on hosts with system packages.
        self.executable(self.tools / "ls", "#!/bin/bash\nexit 1\n")

    @staticmethod
    def executable(path, content="#!/bin/bash\nexit 0\n"):
        path.write_text(content, encoding="utf-8")
        path.chmod(0o755)

    def keg(self, formula, missing=None):
        directory = self.root / formula / "bin"
        directory.mkdir(parents=True)
        for tool in ("pg_ctl", "initdb", "postgres"):
            if tool != missing:
                self.executable(directory / tool)
        return str(directory)

    def discover(self, formulae):
        self.environment["CHECK_FORMULAE"] = "\n".join(formulae)
        return subprocess.run(
            ["/bin/bash", "-c", f"{DISCOVERY}\nfind_bin\n"],
            env=self.environment,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_finds_a_keg_only_version_not_in_the_old_list(self):
        expected = self.keg("postgresql@19")
        result = self.discover(["postgresql@19"])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), expected)

    def test_prefers_newest_numeric_version_regardless_of_listing_order(self):
        formulae = ["postgresql@16", "postgresql@19", "postgresql@100", "postgresql@18"]
        for formula in formulae:
            self.keg(formula)
        result = self.discover(formulae)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), str(self.root / "postgresql@100/bin"))

    def test_rejects_older_versions_and_names_outside_the_formula_pattern(self):
        formulae = ["postgresql@15", "postgresql@19-extra", "other@99"]
        for formula in formulae:
            self.keg(formula)
        result = self.discover(formulae)
        self.assertEqual(result.returncode, 1)
        self.assertIn("Install PostgreSQL 16 or later", result.stderr)

    def test_falls_back_to_the_next_complete_keg_for_each_missing_tool(self):
        expected = self.keg("postgresql@16")
        for version, missing in [(19, "pg_ctl"), (20, "initdb"), (21, "postgres")]:
            self.keg(f"postgresql@{version}", missing)
        result = self.discover(["postgresql@16", "postgresql@19", "postgresql@20", "postgresql@21"])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), expected)

    def test_path_still_takes_precedence(self):
        self.executable(self.tools / "pg_ctl")
        self.keg("postgresql@19")
        result = self.discover(["postgresql@19"])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), str(self.tools))

    def test_empty_and_failed_homebrew_discovery_report_missing_tools(self):
        for failure in ("0", "1"):
            with self.subTest(failure=failure):
                self.environment["CHECK_BREW_FAILURE"] = failure
                result = self.discover([])
                self.assertEqual(result.returncode, 1)
                self.assertIn("Install PostgreSQL 16 or later", result.stderr)


if __name__ == "__main__":
    unittest.main()

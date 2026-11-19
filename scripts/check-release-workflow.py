from pathlib import Path


workflow = Path(".github/workflows/release.yml").read_text(encoding="utf-8")
tag_gate = "if: github.event_name == 'push' && github.ref_type == 'tag'"

if workflow.count(tag_gate) != 5:
    raise SystemExit("Each signing, update feed, and publishing job must require a pushed tag.")

dispatch_rejection = '"$GITHUB_EVENT_NAME" == workflow_dispatch && "$GITHUB_REF_TYPE" == tag'
if dispatch_rejection not in workflow:
    raise SystemExit("Manual runs must reject tag refs.")

publish = workflow[workflow.index("  publish:\n") :]
if "gh release create" not in publish or tag_gate not in publish:
    raise SystemExit("The public release command must remain behind the pushed-tag gate.")

if "GH_REPO: ${{ github.repository }}" not in publish:
    raise SystemExit("The release job must provide repository context without a checkout.")

feed = workflow[workflow.index("  update-feed:\n") : workflow.index("  publish:\n")]
if "environment: release" not in feed or "secrets.UPDATE_SIGNING_PRIVATE_KEY" not in feed:
    raise SystemExit("The private update key must remain inside the protected release environment.")
if workflow.count("secrets.UPDATE_SIGNING_PRIVATE_KEY") != 2:
    raise SystemExit("Only the protected update feed steps may receive the private update key.")
if "secrets.UPDATE_SIGNING_PRIVATE_KEY" in workflow[: workflow.index("  update-feed:\n")]:
    raise SystemExit("Pull request and unsigned package jobs must not receive the private update key.")
if "SecurityMode.Strict" in workflow:
    raise SystemExit("Runtime signature policy belongs in compiled code, not workflow text.")
for platform in ("linux", "windows", "macos"):
    if f'--output-file-name "$prefix-{platform}"' not in feed:
        raise SystemExit(f"The signed update feed must include {platform}.")
if workflow.count("DroverReleaseVersion") != 3:
    raise SystemExit("Every packaged desktop build must carry the release channel version.")

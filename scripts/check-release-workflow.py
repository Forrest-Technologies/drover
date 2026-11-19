from pathlib import Path


workflow = Path(".github/workflows/release.yml").read_text(encoding="utf-8")
tag_gate = "if: github.event_name == 'push' && github.ref_type == 'tag'"

if workflow.count(tag_gate) != 4:
    raise SystemExit("Each signing and publishing job must require a pushed tag.")

dispatch_rejection = '"$GITHUB_EVENT_NAME" == workflow_dispatch && "$GITHUB_REF_TYPE" == tag'
if dispatch_rejection not in workflow:
    raise SystemExit("Manual runs must reject tag refs.")

publish = workflow[workflow.index("  publish:\n") :]
if "gh release create" not in publish or tag_gate not in publish:
    raise SystemExit("The public release command must remain behind the pushed-tag gate.")

if "GH_REPO: ${{ github.repository }}" not in publish:
    raise SystemExit("The release job must provide repository context without a checkout.")

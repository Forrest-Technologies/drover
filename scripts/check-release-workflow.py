from pathlib import Path
import re


workflow = Path(".github/workflows/release.yml").read_text(encoding="utf-8")
docs = Path("docs/desktop-installers.md").read_text(encoding="utf-8")
tag_gate = "if: github.event_name == 'push' && github.ref_type == 'tag'"

def jobs(text):
    lines = text.splitlines()
    start = next(index for index, line in enumerate(lines) if line == "jobs:")
    found = {}
    current = None
    body = []
    for line in lines[start + 1 :]:
        if line.startswith("  ") and not line.startswith("   ") and line.endswith(":"):
            if current is not None:
                found[current] = "\n".join(body)
            current = line.strip()[:-1]
            body = [line]
        elif current is not None:
            body.append(line)
    if current is not None:
        found[current] = "\n".join(body)
    return found


parsed = jobs(workflow)

for name in ("linux-finalize", "windows-finalize", "macos-finalize", "update-feed", "publish"):
    if tag_gate not in parsed[name]:
        raise SystemExit(f"{name} must require a pushed tag.")

dispatch_rejection = '"$GITHUB_EVENT_NAME" == workflow_dispatch && "$GITHUB_REF_TYPE" == tag'
if dispatch_rejection not in workflow:
    raise SystemExit("Manual runs must reject tag refs.")

publish = parsed["publish"]
if "gh release create" not in publish or tag_gate not in publish:
    raise SystemExit("The public release command must remain behind the pushed-tag gate.")

if "GH_REPO: ${{ github.repository }}" not in publish:
    raise SystemExit("The release job must provide repository context without a checkout.")

def reject_wide_secrets(parsed_jobs):
    secret_names = re.compile(r"secrets\.([A-Za-z0-9_]+)")
    for name, body in parsed_jobs.items():
        for secret in secret_names.findall(body):
            if secret == "GITHUB_TOKEN":
                continue
            if "environment: release" not in body or tag_gate not in body:
                raise SystemExit(
                    f"{name} references secrets.{secret} outside a tag-gated release environment job."
                )


reject_wide_secrets(parsed)

leaked = False
try:
    reject_wide_secrets(
        jobs(
            "jobs:\n"
            "  leak:\n"
            "    runs-on: ubuntu-24.04\n"
            "    steps:\n"
            "      - run: echo ${{ secrets.LEAKED }}\n"
        )
    )
except SystemExit:
    leaked = True
if not leaked:
    raise SystemExit("A secret outside the release environment must fail this check.")

for name in ("source", "linux-build", "windows-build", "macos-build"):
    body = parsed[name]
    if "id-token:" in body or "attestations:" in body or "actions/attest-" in body:
        raise SystemExit(f"{name} must not mint attestations.")

for name in ("source-attest", "linux-attest", "windows-attest", "macos-attest"):
    body = parsed[name]
    if "dotnet " in body or "scripts/package" in body:
        raise SystemExit(f"{name} must only download and attest.")
    if "id-token: write" not in body or "attestations: write" not in body:
        raise SystemExit(f"{name} must hold attestation permissions.")
    if "secrets." in body or "environment:" in body:
        raise SystemExit(f"{name} must not read secrets or enter an environment.")

for name, label in (
    ("linux-finalize", "Linux"),
    ("windows-finalize", "Windows"),
    ("macos-finalize", "macOS"),
):
    body = parsed[name]
    if f"{label} signing secrets are incomplete." not in body:
        raise SystemExit(f"{name} must fail when its signing secret set is partial.")
    if "Publishing the unsigned package." not in body:
        raise SystemExit(f"{name} must still publish an unsigned package when every secret is absent.")

linux = parsed["linux-finalize"]
if "GNUPGHOME" not in linux or "--passphrase-fd" not in linux or "trap cleanup EXIT" not in linux:
    raise SystemExit("Linux signing must use a throwaway GNUPGHOME, a passphrase file descriptor, and an EXIT trap.")

windows = parsed["windows-finalize"]
if "finally" not in windows or "Remove-Item $certificate -Force" not in windows:
    raise SystemExit("Windows signing must delete the certificate in a finally block.")
if re.search(r"(?m)(^|[\s])/p([\s]|$)", windows):
    raise SystemExit("signtool must not take the certificate password on the command line.")

macos = parsed["macos-finalize"]
if "trap cleanup EXIT" not in macos or "drover-signing.p12" not in macos or "AuthKey.p8" not in macos:
    raise SystemExit("macOS signing must delete the certificate, the notary key, and the keychain on exit.")

for needle in (
    "--source-ref refs/tags/v1.2.3",
    "--signer-workflow Forrest-Technologies/drover/.github/workflows/release.yml",
    "The attestation is the trust anchor.",
    '"custom_branch_policies": true',
    '"name": "v*"',
    '"type": "tag"',
):
    if needle not in docs:
        raise SystemExit(f"Desktop installer docs must keep this release check: {needle}")

feed = parsed["update-feed"]
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

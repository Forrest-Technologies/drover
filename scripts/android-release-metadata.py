import os
import re


def metadata(event, ref, protected, run_number):
    code = int(run_number)
    if not 1 <= code <= 2100000000:
        raise ValueError("Android version code must be between 1 and 2100000000.")
    result = dict(display=f"0.1.{code}", code=str(code), tag="", channel="none")
    if event != "push":
        return result
    if protected != "true":
        raise ValueError("Only protected branches or tags may publish Android releases.")
    if ref in ("refs/heads/develop", "refs/heads/main"):
        preview = ref == "refs/heads/develop"
        result["channel"] = "preview" if preview else "stable"
        if preview:
            result["display"] += f"-preview.{code}"
        result["tag"] = f"android-v{result['display']}"
    else:
        match = re.fullmatch(r"refs/tags/v(\d+\.\d+\.\d+)-android-preview\.[1-9]\d*", ref)
        if not match:
            raise ValueError("Unsupported Android release ref.")
        result.update(display=match[1], tag=ref.removeprefix("refs/tags/"), channel="preview")
    return result


if __name__ == "__main__":
    values = metadata(os.environ["GITHUB_EVENT_NAME"], os.environ["GITHUB_REF"],
                      os.environ["GITHUB_REF_PROTECTED"], os.environ["GITHUB_RUN_NUMBER"])
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        for key, value in values.items():
            print(f"{key}={value}", file=output)

#!/usr/bin/env python3
"""Check Android Back on the installed development APK, on the first screen a fresh install shows.

The emulator has no server, so only the first screen can be reached. Back there must leave the app the way Android
leaves any app, without a crash. Going back between screens and closing dialogs and rails first is covered by the
navigator's tests in tests/Drover.Presentation.Tests.
"""

import re
import subprocess
import time


PACKAGE = "com.forresttechnologies.drover.client"


def shell(*args):
    return subprocess.check_output(["adb", "shell", *args], text=True).strip()


def resumed():
    activities = shell("dumpsys", "activity", "activities")
    found = re.search(r"topResumedActivity=ActivityRecord\{\S+ u0 (\S+)/", activities)
    return found.group(1) if found else None


def wait_until(condition, message):
    for _ in range(40):
        if condition():
            return
        time.sleep(0.25)
    raise AssertionError(message)


def main():
    shell("am", "force-stop", PACKAGE)
    subprocess.run(["adb", "logcat", "-c"], check=True)
    activity = shell("cmd", "package", "resolve-activity", "--brief", PACKAGE).splitlines()[-1]
    shell("am", "start", "-W", "-n", activity)
    wait_until(lambda: resumed() == PACKAGE, "Drover did not come to the front.")
    # Give the interface time to draw its first screen, so Back reaches the app and not the splash screen.
    time.sleep(3)

    shell("input", "keyevent", "KEYCODE_BACK")

    wait_until(lambda: resumed() not in (None, PACKAGE), "Back on the first screen did not leave Drover.")
    crashes = subprocess.check_output(["adb", "logcat", "-d", "-b", "crash"], text=True)
    if PACKAGE in crashes:
        raise AssertionError(f"Drover crashed on Back:\n{crashes}")
    print("Android Back on the first screen left Drover without a crash.")


if __name__ == "__main__":
    main()

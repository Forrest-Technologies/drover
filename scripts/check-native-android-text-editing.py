#!/usr/bin/env python3
"""Check the development APK on a fresh Android emulator with the test keyboard installed."""

import os
import re
import shlex
import struct
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path


PACKAGE = "com.forresttechnologies.drover.client"
IME_PACKAGE = "org.drover.tests.keyboard"
IME = f"{IME_PACKAGE}/.TestKeyboard"
DUMP = "/data/local/tmp/drover-text-editing.xml"


def shell(*args):
    return subprocess.check_output(["adb", "shell", *args], text=True).strip()


def wait_for_boot(timeout=90):
    """Wait until the device answers a shell command after adbd restarts as root."""
    deadline = time.monotonic() + timeout

    while True:
        state = subprocess.run(["adb", "get-state"], text=True, capture_output=True)
        booted = subprocess.run(["adb", "shell", "getprop", "sys.boot_completed"], text=True, capture_output=True)
        if (state.returncode == 0 and state.stdout.strip() == "device"
                and booted.returncode == 0 and booted.stdout.strip() == "1"):
            return
        if time.monotonic() >= deadline:
            raise AssertionError("The Android device did not come back online after adb root.")
        time.sleep(1)


def capture(*args):
    """Capture a screen once, and once more if the device dropped offline mid-capture."""
    for attempt in range(2):
        result = subprocess.run(["adb", "exec-out", *args], capture_output=True)
        offline = b"device offline" in result.stderr or b"device not found" in result.stderr
        if result.returncode == 0 and result.stdout and not offline:
            return result.stdout
        if attempt == 0 and offline:
            wait_for_boot()
    detail = result.stderr.decode("utf-8", "replace").strip()
    raise AssertionError(f"Could not capture {' '.join(args)} from Android: {detail}")


def start_app():
    shell("am", "force-stop", PACKAGE)
    activity = shell("cmd", "package", "resolve-activity", "--brief", PACKAGE).splitlines()[-1]
    shell("am", "start", "-W", "-n", activity)


def find_address():
    shell("rm", "-f", DUMP)
    shell("uiautomator", "dump", DUMP)
    for node in ET.fromstring(shell("cat", DUMP)).iter("node"):
        if (node.get("package") == PACKAGE and node.get("class") == "android.widget.EditText"
                and node.get("content-desc") == "Server address"):
            return node
    raise AssertionError("Could not find the Server address editor on the connection screen.")


def address_bounds():
    return tuple(map(int, re.findall(r"\d+", find_address().attrib["bounds"])))


def expect(text):
    actual = find_address().get("text")
    if actual != text:
        raise AssertionError(f"Expected the Server address to contain {text!r}, got {actual!r}.")


def expect_visible_keyboard():
    for _ in range(5):
        shell("uiautomator", "dump", "--windows", DUMP)
        nodes = ET.fromstring(shell("cat", DUMP)).iter("node")
        if any(node.get("package") == IME_PACKAGE and node.get("text") == "Drover test keyboard" for node in nodes):
            return
    raise AssertionError("Tapping the Server address did not open the on-screen keyboard.")


def ime_edit(*args):
    # Retry only while Android is connecting the keyboard to the editor.
    for _ in range(20):
        result = shell("am", "broadcast", "-a", f"{IME_PACKAGE}.EDIT", "-p", IME_PACKAGE, *args)
        if "result=2" in result:
            return
        time.sleep(0.25)
    raise AssertionError("The test keyboard did not connect to the editor.")


def check_edits():
    start_app()
    left, top, right, bottom = address_bounds()
    shell("input", "tap", str((left + right) // 2), str((top + bottom) // 2))
    expect_visible_keyboard()

    ime_edit("--es", "text", "abc")
    expect("abc")
    shell("input", "keyevent", "KEYCODE_DEL")
    expect("ab")
    ime_edit("--es", "text", "d")
    expect("abd")

    shell("input", "text", "c")
    expect("abdc")
    ime_edit("--ei", "delete", "1")
    expect("abd")
    ime_edit("--es", "text", "a")
    expect("abda")

    ime_edit("--ei", "delete", "4")
    expect("")
    ime_edit("--es", "text", "d")
    expect("d")
    ime_edit("--ei", "delete", "1")

    ime_edit("--es", "compose", "http")
    expect("http")
    ime_edit("--es", "compose", "htt")
    expect("htt")
    ime_edit("--es", "compose", "https")
    expect("https")
    ime_edit("--ez", "finish", "true")
    ime_edit("--es", "text", "://drover.example.org")
    expect("https://drover.example.org")

    ime_edit("--ei", "select", "14")
    shell("input", "keyevent", "KEYCODE_DEL")
    expect("https://drove.example.org")
    ime_edit("--es", "text", "r")
    expect("https://drover.example.org")
    print("Android server-address editing passed with hardware keys, composing text, and committed text.")


def screenshot(name):
    folder = Path("artifacts/android-layout")
    folder.mkdir(parents=True, exist_ok=True)
    (folder / f"{name}.png").write_bytes(capture("screencap", "-p"))


def system_bar_edge(displays, bar, edge):
    frame = re.search(
        rf"(?:mType|type)={bar} (?:mFrame|frame)=\[\d+,(\d+)\]\[\d+,(\d+)\][^\r\n}}]*"
        r"\b(?:mVisible|visible)=true\b",
        displays,
    )
    if frame is None:
        raise AssertionError(f"Could not read the visible {bar} frame from Android.")
    return int(frame.group(edge))


def check_yard_edges(theme):
    displays = shell("dumpsys", "window", "displays")
    status_bottom = system_bar_edge(displays, "statusBars", 2)
    navigation_top = system_bar_edge(displays, "navigationBars", 1)
    pixels = capture("screencap")
    width, height, format_id, _ = struct.unpack_from("<4I", pixels)
    if (width, height, format_id) != (720, 1600, 1):
        raise AssertionError(f"Expected a 720 × 1600 RGBA emulator image, got {width} × {height} format {format_id}.")

    # The yard dots sit on a 16 dp grid. Sample the center away from the status icons.
    # The strongest light pixels have two observed blends over the same background.
    light = {bytes((223, 227, 234, 255)), bytes((227, 230, 237, 255))}
    dots = {bytes((28, 36, 49, 255))} if theme == "dark" else light
    pitch = 32

    def has_dots(y):
        row = 16 + y * width * 4
        matches = {
            x for x in range(width // 3, width * 2 // 3)
            if pixels[row + x * 4:row + (x + 1) * 4] in dots
        }
        return any(x + pitch in matches and x + 2 * pitch in matches for x in matches)

    top_rows = [y for y in range(status_bottom + pitch) if has_dots(y)]
    bottom_rows = [y for y in range(navigation_top - pitch, height) if has_dots(y)]
    if not top_rows or min(top_rows) < status_bottom or not bottom_rows or max(bottom_rows) >= navigation_top:
        raise AssertionError(
            f"The {theme} yard must start below y={status_bottom} and end above y={navigation_top}. "
            f"Found dot rows {top_rows} at the top and {bottom_rows} at the bottom."
        )
    print(f"Android {theme} safe area passed with yard rows {min(top_rows)} and {max(bottom_rows)} "
          f"between system bars at {status_bottom} and {navigation_top}.")


def check_safe_area():
    # Use a 360 dp viewport for both themes.
    shell("wm", "size", "720x1600")
    shell("wm", "density", "320")
    try:
        start_app()
        normal_top = address_bounds()[1]
        screenshot("360-dark")
        check_yard_edges("dark")

        # The fresh emulator uses a debuggable system image. Seed the app's saved light choice before it launches.
        identity = subprocess.run(["adb", "shell", "id"], text=True, capture_output=True)
        if identity.returncode != 0 or "uid=0(root)" not in identity.stdout:
            subprocess.run(["adb", "root"], check=True)
            wait_for_boot()
        shell("am", "force-stop", PACKAGE)
        data = f"/data/user/0/{PACKAGE}"
        preferences = f"{data}/shared_prefs"
        path = f"{preferences}/{PACKAGE}_preferences.xml"
        fixture = '<map><string name="drover.theme">System.String:light</string></map>'
        shell(f"mkdir -p {preferences} && printf '%s' {shlex.quote(fixture)} > {path} "
              f"&& chown -R $(stat -c %u:%g {data}) {preferences} && restorecon -RF {preferences}")
        start_app()
        light_top = address_bounds()[1]
        screenshot("360-light")
        check_yard_edges("light")
        if abs(light_top - normal_top) > 2:
            raise AssertionError("Changing the theme moved the server address relative to the safe area.")
    finally:
        # A device that went offline must not hide the assertion that found it.
        try:
            wait_for_boot()
            shell("wm", "size", "reset")
            shell("wm", "density", "reset")
        except Exception as error:
            if sys.exc_info()[0] is None:
                raise
            print(f"Could not reset the Android display after the check: {error}")


def check_unreachable_open():
    """A cold deep link while the server refuses the connection shows the unreachable gate and stays up."""
    shell("am", "force-stop", PACKAGE)
    report = f"/storage/emulated/0/Android/data/{PACKAGE}/files/unreachable-open.txt"
    shell("rm", "-f", report)
    activity = shell("cmd", "package", "resolve-activity", "--brief", PACKAGE).splitlines()[-1]
    shell("am", "start", "-W", "-n", activity, "--ez", "drover.unreachable_open", "true")
    for _ in range(100):
        listing = subprocess.run(["adb", "shell", "cat", report], text=True, capture_output=True)
        text = listing.stdout
        if "Unreachable open check passed" in text:
            print(text.strip())
            return
        if "Unreachable open check failed" in text:
            raise AssertionError(text.strip())
        time.sleep(1)
    log = subprocess.run(
        ["adb", "logcat", "-d", "-e", "Unreachable open check"],
        text=True,
        capture_output=True,
    ).stdout
    raise AssertionError(log.strip() or "The unreachable open check did not report a result.")


def check_field_input():
    """Username, password, and address fields expose Android autofill hints, and a long paste is cut."""
    shell("am", "force-stop", PACKAGE)
    activity = shell("cmd", "package", "resolve-activity", "--brief", PACKAGE).splitlines()[-1]
    # The check ends the process from inside launch, so start can report a crash after the report is written.
    subprocess.run(
        ["adb", "shell", "am", "start", "-W", "-n", activity, "--ez", "drover.field_input", "true"],
        text=True,
        capture_output=True,
        check=False,
    )
    report = f"/storage/emulated/0/Android/data/{PACKAGE}/files/field-input.txt"
    for _ in range(30):
        listing = subprocess.run(["adb", "shell", "cat", report], text=True, capture_output=True)
        text = listing.stdout
        if "Field input check passed" in text:
            print("Android fields carried autofill hints, and a paste past the limit was cut with no error.")
            return
        if "Field input check failed" in text:
            raise AssertionError(text.strip())
        time.sleep(1)
    log = subprocess.run(
        ["adb", "logcat", "-d", "-e", "Field input check"],
        text=True,
        capture_output=True,
    ).stdout
    raise AssertionError(log.strip() or "The field input check did not report a result.")


def main():
    if os.environ.get("DROVER_UNREACHABLE_OPEN_ONLY") == "1":
        check_unreachable_open()
        return

    mode = sys.argv[1] if len(sys.argv) > 1 else "--all"
    if mode in ("--all", "--editing"):
        previous = shell("settings", "get", "secure", "default_input_method")
        show_with_hardware = shell("settings", "get", "secure", "show_ime_with_hard_keyboard")
        shell("ime", "enable", IME)
        try:
            shell("settings", "put", "secure", "show_ime_with_hard_keyboard", "1")
            shell("ime", "set", IME)
            check_edits()
        finally:
            if previous and previous != "null":
                shell("ime", "set", previous)
            shell("ime", "disable", IME)
            if show_with_hardware == "null":
                shell("settings", "delete", "secure", "show_ime_with_hard_keyboard")
            else:
                shell("settings", "put", "secure", "show_ime_with_hard_keyboard", show_with_hardware)
        check_safe_area()
    if mode in ("--all", "--field-input"):
        check_field_input()


if __name__ == "__main__":
    main()

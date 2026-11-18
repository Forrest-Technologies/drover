#!/usr/bin/env python3
"""Check the development APK on a fresh Android emulator with the test keyboard installed."""

import re
import subprocess
import time
import xml.etree.ElementTree as ET


PACKAGE = "com.forresttechnologies.drover.client"
IME_PACKAGE = "org.drover.tests.keyboard"
IME = f"{IME_PACKAGE}/.TestKeyboard"
DUMP = "/data/local/tmp/drover-text-editing.xml"


def shell(*args):
    return subprocess.check_output(["adb", "shell", *args], text=True).strip()


def find_address():
    shell("uiautomator", "dump", DUMP)
    for node in ET.fromstring(shell("cat", DUMP)).iter("node"):
        if (node.get("package") == PACKAGE and node.get("class") == "android.widget.EditText"
                and node.get("content-desc") == "Server address"):
            return node
    raise AssertionError("Could not find the Server address editor on the connection screen.")


def expect(text):
    actual = find_address().get("text")
    if actual != text:
        raise AssertionError(f"Expected the Server address to contain {text!r}, got {actual!r}.")


def ime_edit(*args):
    # Retry only while Android is connecting the keyboard to the editor.
    for _ in range(20):
        result = shell("am", "broadcast", "-a", f"{IME_PACKAGE}.EDIT", "-p", IME_PACKAGE, *args)
        if "result=2" in result:
            return
        time.sleep(0.25)
    raise AssertionError("The test keyboard did not connect to the editor.")


def check_edits():
    shell("am", "force-stop", PACKAGE)
    activity = shell("cmd", "package", "resolve-activity", "--brief", PACKAGE).splitlines()[-1]
    shell("am", "start", "-W", "-n", activity)
    address = find_address()
    left, top, right, bottom = map(int, re.findall(r"\d+", address.attrib["bounds"]))
    shell("input", "tap", str((left + right) // 2), str((top + bottom) // 2))

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


def main():
    previous = shell("settings", "get", "secure", "default_input_method")
    shell("ime", "enable", IME)
    try:
        shell("ime", "set", IME)
        check_edits()
    finally:
        if previous and previous != "null":
            shell("ime", "set", previous)
        shell("ime", "disable", IME)


if __name__ == "__main__":
    main()

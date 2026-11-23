"""No emulator needed: prove the safe-area check measures only a settled frame."""

import importlib.util
import io
import struct
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).with_name("check-native-android-text-editing.py")
SPEC = importlib.util.spec_from_file_location("android_text_editing_check", SCRIPT)
check = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(check)

WIDTH = 720
HEIGHT = 1600
HEADER = struct.pack("<4I", WIDTH, HEIGHT, 1, 0)
DARK_DOT = bytes((28, 36, 49, 255))
PIXEL_OFFSET = 16
PITCH = 32
DOT_X = (256, 288, 320)
DISPLAYS = (
    "  mType=statusBars mFrame=[0,0][720,48] mVisible=true\n"
    "  mType=navigationBars mFrame=[0,1504][720,1600] mVisible=true"
)


def frame(*dot_rows):
    """Build a recorded 720x1600 RGBA capture with yard dots on the given rows."""
    pixels = bytearray(HEADER + bytes(WIDTH * HEIGHT * 4))
    for y in dot_rows:
        for x in DOT_X:
            offset = PIXEL_OFFSET + y * WIDTH * 4 + x * 4
            pixels[offset:offset + 4] = DARK_DOT
    return bytes(pixels)


class SettledSafeAreaTests(unittest.TestCase):
    def the_check_measures_only_after_two_identical_captures(self):
        # The first capture is mid-layout: yard dots at the top but none at the bottom inset.
        unsettled = frame(63, 64)
        settled = frame(63, 64, 1480)
        captures = iter([unsettled, settled, settled])

        with patch.object(check, "capture", side_effect=lambda *args: next(captures)), \
                patch.object(check, "shell", return_value=DISPLAYS):
            report = io.StringIO()
            with redirect_stdout(report):
                check.check_yard_edges("dark")

        self.assertIn("1480", report.getvalue())
        self.assertEqual(list(captures), [])

    def the_check_reports_when_frames_never_settle(self):
        captures = iter(frame(63, 64, y) for y in range(1472, 1482))

        with patch.object(check, "capture", side_effect=lambda *args: next(captures)), \
                patch.object(check, "shell", return_value=DISPLAYS):
            with self.assertRaisesRegex(AssertionError, "did not settle"):
                check.check_yard_edges("dark")

    def the_check_refuses_a_single_moving_frame(self):
        captures = iter([frame(1), frame(2)]).__next__
        with self.assertRaisesRegex(AssertionError, "did not settle"):
            check.settled_capture(captures, max_tries=2)


if __name__ == "__main__":
    loader = unittest.TestLoader()
    loader.testMethodPrefix = "the_check_"
    unittest.main(testLoader=loader)

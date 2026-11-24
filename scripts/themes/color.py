"""Contrast and color mixing, the same formulas as Contrast.Ratio in the tests and Rgb.MixWith in the app."""

import math


def rgb(hex_color):
    value = hex_color.lstrip("#")
    return tuple(int(value[index:index + 2], 16) for index in (0, 2, 4))


def _linear(channel):
    channel /= 255
    return channel / 12.92 if channel <= 0.04045 else ((channel + 0.055) / 1.055) ** 2.4


def _encoded(channel):
    channel = min(1.0, max(0.0, channel))
    value = 12.92 * channel if channel <= 0.0031308 else 1.055 * channel ** (1 / 2.4) - 0.055
    return round(value * 255)


def luminance(hex_color):
    red, green, blue = (_linear(channel) for channel in rgb(hex_color))
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue


def ratio(first, second):
    lighter, darker = sorted((luminance(first), luminance(second)), reverse=True)
    return (lighter + 0.05) / (darker + 0.05)


def _oklab(hex_color):
    red, green, blue = (_linear(channel) for channel in rgb(hex_color))
    long = 0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue
    medium = 0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue
    short = 0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue
    long, medium, short = (math.copysign(abs(value) ** (1 / 3), value) for value in (long, medium, short))
    return (0.2104542553 * long + 0.7936177850 * medium - 0.0040720468 * short,
            1.9779984951 * long - 2.4285922050 * medium + 0.4505937099 * short,
            0.0259040371 * long + 0.7827717662 * medium - 0.8086757660 * short)


def _from_oklab(lightness, a, b):
    long = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3
    medium = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3
    short = (lightness - 0.0894841775 * a - 1.2914855480 * b) ** 3
    red = 4.0767416621 * long - 3.3077115913 * medium + 0.2309699292 * short
    green = -1.2684380046 * long + 2.6097574011 * medium - 0.3413193965 * short
    blue = -0.0041960863 * long - 0.7034186147 * medium + 1.7076147010 * short
    return "#%02X%02X%02X" % (_encoded(red), _encoded(green), _encoded(blue))


def mix(color, other, share):
    """color.MixWith(other, share): share of the first color, the rest of the second, in OKLab."""
    first, second = _oklab(color), _oklab(other)
    return _from_oklab(*(left * share + right * (1 - share) for left, right in zip(first, second)))

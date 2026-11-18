"""Check a native sign-in against both containers in a Compose replacement."""

from http.client import HTTPConnection
from http.cookies import SimpleCookie
import json
import re
import secrets
import subprocess


def request(method, path, status, headers=None, payload=None):
    headers = dict(headers or {})
    body = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        body = json.dumps(payload)

    connection = HTTPConnection("127.0.0.1", 8080, timeout=15)
    try:
        connection.request(method, path, body=body, headers=headers)
        response = connection.getresponse()
        if response.status != status:
            raise SystemExit(f"{method} {path} returned {response.status}, expected {status}")
        content = response.read()
        return json.loads(content) if content else None, response.headers
    finally:
        connection.close()


def cookie_headers(headers):
    cookies = SimpleCookie()
    for value in headers.get_all("Set-Cookie", []):
        cookies.load(value)
    return {
        "Cookie": "; ".join(f"{key}={cookie.coded_value}" for key, cookie in cookies.items()),
        "X-XSRF-TOKEN": cookies["XSRF-TOKEN"].value,
    }


def verify_session(access_token, user_id):
    session, _ = request("GET", "/api/session", 200, {"Authorization": f"Bearer {access_token}"})
    if session["userId"] != user_id:
        raise SystemExit("The native session returned a different person")


def main():
    logs = subprocess.check_output(["docker", "compose", "logs", "--no-color", "drover"], text=True)
    code = re.search(r"enter the setup code ([A-Z2-9]{4}(?:-[A-Z2-9]{4}){2})", logs)
    if not code:
        raise SystemExit("First-run setup code was missing from the Compose test server")

    _, antiforgery = request("GET", "/api/antiforgery", 204)
    person, signed_in = request("POST", "/api/setup", 201, cookie_headers(antiforgery), {
        "email": "native-smoke@example.test",
        "displayName": "Native smoke test",
        "password": secrets.token_urlsafe(24),
        "setupCode": code[1],
    })
    issued, _ = request("POST", "/api/native/session", 200, cookie_headers(signed_in))
    verify_session(issued["accessToken"], person["userId"])

    subprocess.run(["docker", "compose", "down"], check=True)
    subprocess.run(["docker", "compose", "up", "-d", "--wait", "--wait-timeout", "180", "--no-build"], check=True)

    verify_session(issued["accessToken"], person["userId"])
    renewed, _ = request("POST", "/api/native/refresh", 200, payload={"refreshToken": issued["refreshToken"]})
    verify_session(renewed["accessToken"], person["userId"])
    print("The native session and its refresh credential survived container replacement.")


if __name__ == "__main__":
    main()

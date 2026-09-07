"""Exercise real loopback delivery. Build the prototype before running this check."""
from pathlib import Path
import statistics
import subprocess
import time
import urllib.error
import urllib.request

here = Path(__file__).resolve().parent
base = "http://127.0.0.1:5187/"
with subprocess.Popen(
    ["dotnet", "run", "-c", "Release", "--no-build", "--project", str(here), "--", "--serve"],
    stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT,
) as server:
    try:
        for attempt in range(100):
            if server.poll() is not None:
                raise RuntimeError("Prototype exited before becoming ready (port 5187 must be free)")
            try:
                urllib.request.urlopen(base + "buffered", timeout=2).read()
                break
            except urllib.error.URLError:
                time.sleep(.1)
        else:
            raise RuntimeError("Prototype did not become ready")
        for path in ["buffered", "streamed?batch=25", "streamed?batch=50", "streamed?batch=100"]:
            for _ in range(3):
                urllib.request.urlopen(base + path, timeout=10).read()
            timings = []
            for _ in range(10):
                start = time.perf_counter()
                with urllib.request.urlopen(base + path, timeout=10) as response:
                    first = response.read(1)
                    ttfb = time.perf_counter() - start
                    data = first + response.read()
                    total = time.perf_counter() - start
                    assert response.headers["Transfer-Encoding"] == "chunked"
                    assert data.startswith(b"<!doctype html>") and data.endswith(b"</body></html>")
                    assert data.count(b'class="game-row ') == 900
                timings.append((ttfb * 1000, total * 1000))
            print(path, "bytes", len(data), "median TTFB ms", round(statistics.median(t[0] for t in timings), 2),
                  "median total ms", round(statistics.median(t[1] for t in timings), 2))
        try:
            urllib.request.urlopen(base + "streamed?batch=0", timeout=10)
        except urllib.error.HTTPError as error:
            assert error.code == 400
        else:
            raise AssertionError("Invalid batch was not rejected")
        with urllib.request.urlopen(base + "streamed?batch=50", timeout=10) as response:
            pieces = []
            while chunk := response.read(8192):
                pieces.append(chunk)
                time.sleep(.002)
            data = b"".join(pieces)
            assert data.count(b'class="game-row ') == 900 and data.endswith(b"</body></html>")
        print("Chunked delivery, complete rows, throttled-reader completion and invalid status passed.")
    finally:
        server.terminate()
        try:
            server.wait(timeout=10)
        except subprocess.TimeoutExpired:
            server.kill()
            server.wait()

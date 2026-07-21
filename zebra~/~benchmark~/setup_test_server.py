"""
Test file server for ZebraYooAsset download benchmark.

Usage:
    cd <this_directory>
    python3 setup_test_server.py          # Generate test files + start server on port 8080
    python3 setup_test_server.py --gen    # Only generate test files
    python3 setup_test_server.py --serve  # Only start server (files must already exist)

Files are generated in ./test_files/
"""

import os
import sys
import random
import http.server
import socketserver

TEST_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "test_files")
PORT = 8080

FILE_SPECS = [
    # (filename, size_in_bytes)
    ("1KB.bin", 1024),
    ("10KB.bin", 10 * 1024),
    ("100KB.bin", 100 * 1024),
    ("500KB.bin", 500 * 1024),
    ("1MB.bin", 1024 * 1024),
    ("5MB.bin", 5 * 1024 * 1024),
    ("10MB.bin", 10 * 1024 * 1024),
    ("50MB.bin", 50 * 1024 * 1024),
    ("100MB.bin", 100 * 1024 * 1024),
]


def generate_files():
    os.makedirs(TEST_DIR, exist_ok=True)
    for name, size in FILE_SPECS:
        path = os.path.join(TEST_DIR, name)
        if os.path.exists(path) and os.path.getsize(path) == size:
            print(f"  skip  {name} ({format_size(size)}) - already exists")
            continue
        print(f"  gen   {name} ({format_size(size)}) ...")
        with open(path, "wb") as f:
            remaining = size
            chunk = 1024 * 1024  # write 1MB at a time
            while remaining > 0:
                write_size = min(chunk, remaining)
                f.write(random.randbytes(write_size))
                remaining -= write_size
    print(f"Done. Files in: {TEST_DIR}")


def format_size(n):
    if n < 1024:
        return f"{n}B"
    elif n < 1024 * 1024:
        return f"{n // 1024}KB"
    else:
        return f"{n // (1024 * 1024)}MB"


def start_server():
    os.chdir(TEST_DIR)
    handler = http.server.SimpleHTTPRequestHandler
    with socketserver.TCPServer(("", PORT), handler) as httpd:
        print(f"Serving {TEST_DIR} on http://localhost:{PORT}")
        print("Press Ctrl+C to stop")
        httpd.serve_forever()


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--gen" in args:
        generate_files()
    elif "--serve" in args:
        start_server()
    else:
        generate_files()
        print()
        start_server()

"""HTTPS stand-in for an Azure DevOps Server collection, serving one project list."""

import base64
import hashlib
import json
import ssl
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PROJECT_ID = "6ce954b1-ce1f-45d1-b94d-e6bf2464ba2c"


class Handler(BaseHTTPRequestHandler):
    def do_GET(self) -> None:
        if not self.path.split("?", 1)[0].endswith("/_apis/projects"):
            self.send_error(404)
            return

        # Names the PAT it was called with, so tests can tell whose credential reached it without ever seeing it.
        credentials = self.headers.get("Authorization", "")
        pat = base64.b64decode(credentials[6:]).decode().split(":", 1)[-1] if credentials.startswith("Basic ") else ""
        body = json.dumps(
            {
                "count": 1,
                "value": [
                    {
                        "id": PROJECT_ID,
                        "name": f"Alpha seen with PAT {hashlib.sha256(pat.encode()).hexdigest()[:8]}",
                        "description": None,
                        "state": "wellFormed",
                        "url": f"https://{self.headers['Host']}/DefaultCollection/_apis/projects/{PROJECT_ID}",
                    }
                ],
            }
        ).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


if __name__ == "__main__":
    certificate, key = sys.argv[1], sys.argv[2]
    port = int(sys.argv[3]) if len(sys.argv) > 3 else 8443
    server = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(certificate, key)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    server.serve_forever()

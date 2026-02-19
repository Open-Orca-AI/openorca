"""
Mock OpenAI-compatible API server for OpenOrca demo GIF recording.

Returns canned SSE streaming responses with text-based <tool_call> tags.
Turn logic is based on the number of "role": "tool" messages in the request.

Usage:
    python demo/mock-server.py [--port 1234]
"""

import json
import sys
import time
from http.server import HTTPServer, BaseHTTPRequestHandler

PORT = 1234
MODEL_ID = "demo-model-7b"
CHUNK_DELAY = 0.03  # seconds between SSE chunks

# --- Canned responses ---

TOOL_CALL_READ_FILE = (
    "Let me start by reading the README to understand this project.\n\n"
    '<tool_call>{"name":"read_file","arguments":{"path":"README.md"}}</tool_call>'
)

TOOL_CALL_LIST_DIR = (
    "Good, I can see the README. Let me also check the project structure.\n\n"
    '<tool_call>{"name":"list_directory","arguments":{"path":"."}}</tool_call>'
)

FINAL_SUMMARY = """\
Here's a summary of the **OpenOrca** project:

**OpenOrca** is an autonomous AI coding agent that runs in your terminal. \
It connects to local LLM servers like LM Studio or Ollama via an OpenAI-compatible API \
and uses **31 built-in tools** to read, write, and execute code autonomously.

**Key highlights:**

- **Autonomous agent loop** — plans, acts, observes, and iterates up to 25 turns per request
- **31 tools** — file I/O, shell execution, git operations, web search, GitHub integration, and sub-agent spawning
- **Works with any local model** — Mistral, Llama, DeepSeek, Qwen, and more
- **Smart tool calling** — auto-detects native function calling support, falls back to text-based `<tool_call>` tags
- **Rich CLI** — streaming output, thinking indicator, slash commands, session management, context compaction
- **Privacy-first** — everything runs locally, no data leaves your machine

The project is structured as a .NET 9 solution with three main assemblies: \
`OpenOrca.Cli` (console UI), `OpenOrca.Core` (domain logic), and `OpenOrca.Tools` (tool implementations). \
It's MIT licensed and designed as a local, open-source alternative to cloud-based AI coding assistants.\
"""


def make_sse_chunk(content: str, finish_reason=None):
    """Build a single SSE data line in OpenAI chat completion format."""
    delta = {}
    if content:
        delta["content"] = content
    choice = {"index": 0, "delta": delta}
    if finish_reason:
        choice["finish_reason"] = finish_reason
    payload = {
        "id": "chatcmpl-demo",
        "object": "chat.completion.chunk",
        "created": int(time.time()),
        "model": MODEL_ID,
        "choices": [choice],
    }
    return f"data: {json.dumps(payload)}\n\n"


def stream_text(wfile, text: str):
    """Stream text word-by-word as SSE chunks."""
    words = text.split(" ")
    for i, word in enumerate(words):
        token = word if i == 0 else " " + word
        chunk = make_sse_chunk(token)
        wfile.write(chunk.encode())
        wfile.flush()
        time.sleep(CHUNK_DELAY)

    # Send final chunk with finish_reason
    final = make_sse_chunk("", finish_reason="stop")
    wfile.write(final.encode())
    wfile.write(b"data: [DONE]\n\n")
    wfile.flush()


class MockHandler(BaseHTTPRequestHandler):
    """Handles /v1/models and /v1/chat/completions."""

    def log_message(self, format, *args):
        """Log to stderr for visibility."""
        print(f"[mock-server] {args[0]}", file=sys.stderr)

    def do_GET(self):
        if self.path == "/v1/models":
            self._handle_models()
        else:
            self.send_error(404)

    def do_POST(self):
        if self.path == "/v1/chat/completions":
            self._handle_completions()
        else:
            self.send_error(404)

    def _handle_models(self):
        body = json.dumps({
            "object": "list",
            "data": [
                {
                    "id": MODEL_ID,
                    "object": "model",
                    "created": int(time.time()),
                    "owned_by": "demo",
                }
            ],
        })
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(body.encode())

    def _handle_completions(self):
        # Read request body
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length)
        body = json.loads(raw) if raw else {}

        messages = body.get("messages", [])
        tool_msg_count = sum(1 for m in messages if m.get("role") == "tool")

        # Pick response based on turn count
        if tool_msg_count == 0:
            response_text = TOOL_CALL_READ_FILE
        elif tool_msg_count == 1:
            response_text = TOOL_CALL_LIST_DIR
        else:
            response_text = FINAL_SUMMARY

        print(
            f"[mock-server] tool_messages={tool_msg_count}, "
            f"responding with {'tool_call' if tool_msg_count < 2 else 'summary'} "
            f"({len(response_text)} chars)",
            file=sys.stderr,
        )

        # Stream SSE response
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "keep-alive")
        self.end_headers()

        stream_text(self.wfile, response_text)


def main():
    port = PORT
    if "--port" in sys.argv:
        idx = sys.argv.index("--port")
        port = int(sys.argv[idx + 1])

    server = HTTPServer(("0.0.0.0", port), MockHandler)
    print(f"[mock-server] Listening on http://0.0.0.0:{port}", file=sys.stderr)
    print(f"[mock-server] Model: {MODEL_ID}", file=sys.stderr)
    print(f"[mock-server] Endpoints: GET /v1/models, POST /v1/chat/completions", file=sys.stderr)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n[mock-server] Shutting down.", file=sys.stderr)
        server.server_close()


if __name__ == "__main__":
    main()

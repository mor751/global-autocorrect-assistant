import argparse
import json
import os
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

# Windows blocks symlinks without admin/Developer Mode, which corrupts the HF cache; force file copies.
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS", "1")
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

try:
    from fastembed import TextEmbedding
    FASTEMBED_IMPORT_OK = True
    FASTEMBED_IMPORT_ERROR = ""
except Exception as exc:
    TextEmbedding = None
    FASTEMBED_IMPORT_OK = False
    FASTEMBED_IMPORT_ERROR = str(exc)

DEFAULT_MODEL = "BAAI/bge-small-en-v1.5"
models = {}
last_error = FASTEMBED_IMPORT_ERROR
_supported_cache = None


def supported_models():
    global _supported_cache, last_error
    if _supported_cache is not None:
        return _supported_cache
    if not FASTEMBED_IMPORT_OK:
        return []
    result = []
    try:
        for entry in TextEmbedding.list_supported_models():
            if isinstance(entry, dict):
                name = entry.get("model") or entry.get("model_name")
                dim = entry.get("dim") or entry.get("dimension")
            else:
                name = getattr(entry, "model", None) or getattr(entry, "model_name", None)
                dim = getattr(entry, "dim", None) or getattr(entry, "dimension", None)
            if name:
                result.append({"model": name, "dim": dim})
        _supported_cache = result
    except Exception as exc:
        last_error = str(exc)
    return result


def supported_model_names():
    return [m["model"] for m in supported_models()]


def get_model(name):
    global last_error
    if not FASTEMBED_IMPORT_OK:
        last_error = f"fastembed import failed: {FASTEMBED_IMPORT_ERROR}"
        raise RuntimeError(last_error)
    names = supported_model_names()
    if names and name not in names:
        last_error = f"Model {name} is not supported in TextEmbedding. Supported models: {', '.join(names)}"
        raise RuntimeError(last_error)
    if name not in models:
        models[name] = TextEmbedding(model_name=name)
    return models[name]


def model_dimension(name):
    model = get_model(name)
    vector = next(iter(model.embed(["query: health check"])))
    return len(vector)


def state(model_name, load_model=False):
    global last_error
    dimension = 0
    model_loaded = model_name in models
    if load_model:
        try:
            dimension = model_dimension(model_name)
            model_loaded = True
            last_error = ""
        except Exception as exc:
            last_error = str(exc)
    elif model_loaded:
        try:
            dimension = model_dimension(model_name)
        except Exception:
            dimension = 0

    return {
        "ok": FASTEMBED_IMPORT_OK and (not load_model or model_loaded),
        "sidecarRunning": True,
        "pythonExecutable": sys.executable,
        "pythonVersion": sys.version.split()[0],
        "fastembedImportOk": FASTEMBED_IMPORT_OK,
        "modelLoaded": model_loaded,
        "modelName": model_name,
        "dimension": dimension,
        "expectedDimension": 384,
        "supportedModels": supported_model_names(),
        "lastError": last_error,
        "timestamp": time.time(),
    }


class Handler(BaseHTTPRequestHandler):
    model_name = DEFAULT_MODEL

    def log_message(self, fmt, *args):
        return

    def do_GET(self):
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        model_name = query.get("model", [self.model_name])[0] or self.model_name

        if parsed.path == "/health":
            self.send_json(state(model_name, load_model=False))
            return
        if parsed.path == "/model-info":
            self.send_json(state(model_name, load_model=True))
            return
        if parsed.path == "/supported-models":
            self.send_json({"ok": True, "supportedModels": supported_models()})
            return
        self.send_json({"ok": False, "lastError": f"unknown endpoint: {parsed.path}"}, status=404)

    def do_POST(self):
        parsed = urlparse(self.path)
        try:
            payload = self.read_json()
            model_name = payload.get("model") or self.model_name
            if parsed.path == "/embed":
                text = str(payload.get("text") or "")
                vectors = embed(model_name, [text], int(payload.get("batchSize") or 32))
                self.send_json({"ok": True, "modelName": model_name, "dimension": len(vectors[0]) if vectors else 0, "vector": vectors[0] if vectors else []})
                return
            if parsed.path == "/embed-batch":
                texts = [str(text) for text in payload.get("texts", [])]
                vectors = embed(model_name, texts, int(payload.get("batchSize") or 32))
                self.send_json({"ok": True, "modelName": model_name, "dimension": len(vectors[0]) if vectors else 0, "vectors": vectors})
                return
            self.send_json({"ok": False, "lastError": f"unknown endpoint: {parsed.path}"}, status=404)
        except Exception as exc:
            global last_error
            last_error = str(exc)
            self.send_json({"ok": False, "lastError": str(exc), **state(self.model_name, load_model=False)}, status=500)

    def read_json(self):
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length).decode("utf-8") if length > 0 else "{}"
        return json.loads(raw)

    def send_json(self, payload, status=200):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def embed(model_name, texts, batch_size):
    global last_error
    model = get_model(model_name)
    vectors = [vector.tolist() for vector in model.embed(texts, batch_size=batch_size)]
    last_error = ""
    return vectors


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--model", default=DEFAULT_MODEL)
    args = parser.parse_args()
    Handler.model_name = args.model
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    print(json.dumps({"ok": True, "url": f"http://{args.host}:{args.port}", "pythonExecutable": sys.executable}), flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()

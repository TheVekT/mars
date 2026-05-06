import os
import sys
import json
import asyncio
import argparse
import ctypes
import platform
import importlib
import importlib.metadata
import subprocess
import ast
import logging
from contextlib import asynccontextmanager

CURRENT_DIR = os.path.dirname(os.path.abspath(__file__))
if CURRENT_DIR not in sys.path:
    sys.path.insert(0, CURRENT_DIR)

import uvicorn
from fastapi import FastAPI, HTTPException, Request, Depends
from fastapi.middleware.cors import CORSMiddleware
from mars.core.http.security import verify_auth_token

from mars.core.http.registry import registry
from mars.core.websocket.registry import ws_registry

LOG_FILE = "server.log"

def setup_logging():
    """Configure logging to file and console with recursion protection."""
    import io

    if sys.platform == "win32":
        safe_stdout = io.TextIOWrapper(sys.__stdout__.buffer, encoding='utf-8', errors='replace')
        safe_stderr = io.TextIOWrapper(sys.__stderr__.buffer, encoding='utf-8', errors='replace')
    else:
        safe_stdout = sys.__stdout__
        safe_stderr = sys.__stderr__

    log_formatter = logging.Formatter('%(asctime)s [%(levelname)s] %(message)s', datefmt='%Y-%m-%d %H:%M:%S')

    file_handler = logging.FileHandler(LOG_FILE, mode='w', encoding='utf-8')
    file_handler.setFormatter(log_formatter)

    console_handler = logging.StreamHandler(safe_stdout)
    console_handler.setFormatter(log_formatter)

    root_logger = logging.getLogger()
    root_logger.setLevel(logging.INFO)
    root_logger.addHandler(file_handler)
    root_logger.addHandler(console_handler)

    class StreamToLogger:
        def __init__(self, logger, level, fallback_stream):
            self.logger = logger
            self.level = level
            self.fallback_stream = fallback_stream
            self._is_logging = False

        def write(self, buf):
            if self._is_logging:
                try:
                    self.fallback_stream.write(buf)
                    self.fallback_stream.flush()
                except Exception:
                    pass
                return

            self._is_logging = True
            try:
                for line in buf.rstrip().splitlines():
                    if not line.strip():
                        continue
                    self.logger.log(self.level, line.rstrip())
            except Exception as e:
                self.fallback_stream.write(f"Logger loop error: {e}\n")
            finally:
                self._is_logging = False

        def flush(self):
            pass

    sys.stdout = StreamToLogger(logging.getLogger('STDOUT'), logging.INFO, safe_stdout)
    sys.stderr = StreamToLogger(logging.getLogger('STDERR'), logging.ERROR, safe_stderr)

logger = logging.getLogger("MARS")

# Module-level reference so the /admin/shutdown endpoint can stop uvicorn cleanly.
_uvicorn_server: "uvicorn.Server | None" = None

@asynccontextmanager
async def lifespan(app):
    logger.info("MARS server started successfully.")
    yield
    logger.info("MARS server is shutting down. Goodbye!")

app = FastAPI(title="MARS Server API", version="1.0.0", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

def is_admin() -> bool:
    """Return True when the current process has administrator privileges."""
    try:
        if platform.system() == "Windows":
            return ctypes.windll.shell32.IsUserAnAdmin() != 0
        else:
            return os.geteuid() == 0
    except Exception:
        return False

def elevate_privileges():
    """Restart the process with elevated privileges when required."""
    if is_admin():
        return

    logger.warning("Insufficient privileges. Requesting elevation...")
    
    if platform.system() == "Windows":
        script = os.path.abspath(sys.argv[0])
        params = ' '.join([f'"{arg}"' for arg in sys.argv[1:]])
        ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, f'"{script}" {params}', None, 1)
        sys.exit(0)
    else:
        logger.info("Please enter the sudo password in the terminal.")
        args = ['sudo', sys.executable] + sys.argv
        os.execlpe('sudo', *args, os.environ)

def install_module_dependencies(module_dir: str, module_name: str):
    """Install dependencies declared anywhere inside a module package."""
    requirements = set()
    for root, _, files in os.walk(module_dir):
        for fn in files:
            if not fn.endswith(".py"):
                continue
            fp = os.path.join(root, fn)
            try:
                with open(fp, "r", encoding="utf-8") as f:
                    tree = ast.parse(f.read(), filename=fp)
            except Exception as e:
                logger.debug(f"Failed to parse {fp}: {e}")
                continue
            for node in ast.walk(tree):
                if isinstance(node, ast.Assign):
                    for target in node.targets:
                        if isinstance(target, ast.Name) and target.id == "__requirements__":
                            if isinstance(node.value, ast.List):
                                for elt in node.value.elts:
                                    if isinstance(elt, ast.Constant) and isinstance(elt.value, str):
                                        requirements.add(elt.value)
    if not requirements:
        return
    for req in sorted(requirements):
        try:
            importlib.metadata.version(req)
            continue
        except importlib.metadata.PackageNotFoundError:
            logger.info(f"Installing missing library '{req}'...")
        try:
            subprocess.check_call([sys.executable, "-m", "pip", "install", req, "--quiet"])
            logger.info(f"Library '{req}' installed successfully.")
        except Exception as e:
            logger.error(f"Failed to install '{req}': {e}")

def load_modules(disabled_modules: list):
    """Discover and import all enabled modules from the modules directory."""
    modules_dir = "modules"
    if not os.path.exists(modules_dir):
        os.makedirs(modules_dir)
        return
    
    for item in os.listdir(modules_dir):
        module_path = os.path.join(modules_dir, item)
        
        if os.path.isdir(module_path) and not item.startswith("__"):
            
            if item in disabled_modules:
                logger.info(f"Skipping disabled module: {item}")
                continue

            init_file = os.path.join(module_path, "__init__.py")
            
            if not os.path.exists(init_file):
                logger.warning(f"Skipping {item}: missing __init__.py")
                continue

            manifest_file = os.path.join(module_path, "manifest.json")
            if os.path.exists(manifest_file):
                try:
                    with open(manifest_file, "r", encoding="utf-8") as f:
                        manifest = json.load(f)
                        logger.info(f"Found module: {manifest.get('name', item)} v{manifest.get('version', '1.0')}")
                except json.JSONDecodeError:
                    logger.warning(f"Invalid manifest.json format in {item}")

            module_name = f"{modules_dir}.{item}"
            
            install_module_dependencies(module_path, module_name)
            
            try:
                importlib.import_module(module_name)
                logger.info(f"Module loaded successfully: {module_name}")
            except Exception as e:
                logger.error(f"Failed to load module {module_name}: {e}")

def main():
    """Run the server CLI entry point."""
    parser = argparse.ArgumentParser(description="MARS Server Daemon")
    parser.add_argument("--host", type=str, default="0.0.0.0", help="IP address to bind the server")
    parser.add_argument("--port", type=int, default=8000, help="Port to bind the server")
    parser.add_argument("--dump-schema", action="store_true", help="Print JSON schema of all modules to stdout and exit")
    parser.add_argument("--disabled-modules", nargs="*", default=[], help="List of modules to disable")
    parser.add_argument("--password", type=str, default="", help="Password (API Key) for client connection")

    args = parser.parse_args()

    setup_logging()
    
    app.state.auth_token = args.password
    elevate_privileges()

    load_modules(args.disabled_modules)

    if args.dump_schema:
        schema = registry.generate_schema()
        sys.__stdout__.write(json.dumps(schema, ensure_ascii=False) + "\n")
        sys.exit(0)

    app.include_router(registry.router, prefix="/api/v1")
    app.include_router(ws_registry.router, prefix="/ws/v1")
    
    @app.get("/api/v1/auth", tags=["Core System"], dependencies=[Depends(verify_auth_token)])
    async def authenticate():
        """Validate the X-MARS-Auth header."""
        return {"status": "success", "message": "Authenticated"}

    @app.get("/api/v1/schema", tags=["Core System"])
    async def get_ui_schema():
        """Return the generated UI schema."""
        return {"status": "success", "schema": registry.generate_schema()}

    @app.get("/api/v1/ws_schema", tags=["Core System"])
    async def get_ws_schema():
        """Return the websocket capabilities schema."""
        return {"status": "success", "schema": ws_registry.generate_schema()}

    @app.post("/admin/shutdown", include_in_schema=False)
    async def admin_shutdown(request: Request):
        """Gracefully stop the server. Accepts requests from localhost only."""
        client_host = request.client.host if request.client else ""
        if client_host not in ("127.0.0.1", "::1", "localhost"):
            raise HTTPException(status_code=403, detail="Forbidden")

        logger.info("[Admin] Shutdown request received from localhost.")

        async def _do_shutdown():
            await asyncio.sleep(0.1)  # let the HTTP response reach the client first
            if _uvicorn_server:
                _uvicorn_server.should_exit = True

        asyncio.create_task(_do_shutdown())
        return {"status": "shutting_down"}

    logger.info(f"MARS server is ready on {args.host}:{args.port}")
    if args.disabled_modules:
        logger.info(f"Disabled modules: {', '.join(args.disabled_modules)}")

    global _uvicorn_server
    config = uvicorn.Config(app, host=args.host, port=args.port, log_config=None, ws_ping_interval=None, ws_ping_timeout=None)
    _uvicorn_server = uvicorn.Server(config)
    _uvicorn_server.run()

if __name__ == "__main__":
    main()
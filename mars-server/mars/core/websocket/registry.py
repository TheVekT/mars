import inspect
from typing import Type, Dict, Any, List
from fastapi import APIRouter

from mars.core.websocket.security import with_ws_auth

def mars_ws_module(name: str, prefix: str, compatibility: List[str] = None, requires_tools: List[str] = None):
    """Mark a class as a WebSocket module and store its metadata."""
    if compatibility is None: compatibility = ["windows"]
    if requires_tools is None: requires_tools = []

    def wrapper(cls):
        cls.__mars_ws_module__ = True
        cls.__mars_name__ = name
        cls.__mars_prefix__ = prefix
        cls.__mars_compatibility__ = compatibility
        cls.__mars_requires_tools__ = requires_tools
        return cls
    return wrapper

def ws_endpoint(path: str):
    """Mark a method as a WebSocket endpoint."""
    def wrapper(func):
        func.__mars_ws_endpoint__ = True
        func.__mars_ws_path__ = path
        return func
    return wrapper

class MarsWSRegistry:
    """Registry for dynamically loaded WebSocket modules."""
    def __init__(self):
        self.router = APIRouter()
        self._modules: Dict[str, Any] = {}

    def register(self, module_cls: Type):
        if not getattr(module_cls, "__mars_ws_module__", False):
            raise ValueError(f"Class {module_cls.__name__} must use the @mars_ws_module decorator")

        prefix = module_cls.__mars_prefix__
        instance = module_cls()
        self._modules[prefix] = instance

        for method_name, method in inspect.getmembers(instance, predicate=inspect.ismethod):
            if getattr(method, "__mars_ws_endpoint__", False):
                full_path = f"{prefix}{method.__mars_ws_path__}"
                secured_endpoint = with_ws_auth(method)
                
                self.router.add_api_websocket_route(full_path, endpoint=secured_endpoint)

    def generate_schema(self) -> List[Dict[str, Any]]:
        """Generate the JSON schema for discovering websocket capabilities."""
        schema = []
        for prefix, instance in self._modules.items():
            cls = instance.__class__
            schema.append({
                "module_name": getattr(cls, "__mars_name__", "Unknown"),
                "prefix": prefix,
                "compatibility": getattr(cls, "__mars_compatibility__", ["windows"]),
                "requires_tools": getattr(cls, "__mars_requires_tools__", [])
            })
        return schema

ws_registry = MarsWSRegistry()
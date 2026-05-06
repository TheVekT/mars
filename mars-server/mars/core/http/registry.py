import inspect
from typing import Type, Dict, Any, List
from fastapi import APIRouter, Depends
from mars.core.http.security import verify_auth_token

class MarsRegistry:
    def __init__(self):
        self.router = APIRouter()
        self._modules: Dict[str, Any] = {}

    def register(self, module_cls: Type):
        """Create a module instance and register its routes in FastAPI."""
        if not getattr(module_cls, "__mars_module__", False):
            raise ValueError(f"Class {module_cls.__name__} must use the @mars_module decorator")

        prefix = module_cls.__mars_prefix__
        module_name = module_cls.__mars_name__
        
        instance = module_cls()
        self._modules[prefix] = instance

        module_router = APIRouter(
            prefix=prefix, 
            tags=[module_name],
            dependencies=[Depends(verify_auth_token)] 
        )

        for method_name, method in inspect.getmembers(instance, predicate=inspect.ismethod):
            func = getattr(method, "__func__", method)
            if getattr(func, "__mars_action__", False):
                module_router.add_api_route(
                    path=f"/{method_name}",
                    endpoint=method,
                    methods=[getattr(func, "__mars_http_method__", "GET")],
                    name=getattr(func, "__mars_label__", method_name)
                )

        self.router.include_router(module_router)

    def generate_schema(self) -> List[Dict[str, Any]]:
        """Generate the JSON schema for all registered modules and their actions."""
        schema = []

        for prefix, instance in self._modules.items():
            module_info = {
                "module_name": instance.__mars_name__,
                "base_route": prefix,
                "compatibility": instance.__mars_compatibility__,
                "requires_tools": instance.__mars_requires_tools__,
                "actions": []
            }

            for method_name, method in inspect.getmembers(instance, predicate=inspect.ismethod):
                func = getattr(method, "__func__", method)
                if getattr(func, "__mars_action__", False):
                    action_data = {
                        "endpoint": f"{prefix}/{method_name}",
                        "method": getattr(func, "__mars_http_method__", "GET"),
                        "label": getattr(func, "__mars_label__", method_name),
                        "interaction_type": getattr(func, "__mars_interaction_type__", "read"),
                        "data_type": getattr(func, "__mars_data_type__", "scalar"),
                        "refresh_interval_ms": getattr(func, "__mars_refresh_interval__", 0),
                        "parameters": {}
                    }

                    for key, val in getattr(func, "__mars_params__", {}).items():
                        if key == "bind_source" and callable(val):
                            action_data["parameters"][key] = f"{prefix}/{val.__name__}"
                        elif key == "params_schema" and isinstance(val, list):
                            resolved_schema = []
                            for param in val:
                                param_copy = dict(param)
                                source_endpoint = param_copy.get("source_endpoint")
                                if callable(source_endpoint):
                                    param_copy["source_endpoint"] = f"{prefix}/{source_endpoint.__name__}"
                                resolved_schema.append(param_copy)
                            action_data["parameters"][key] = resolved_schema
                        
                        elif callable(val):
                            try:
                                action_data["parameters"][key] = val()
                            except Exception:
                                action_data["parameters"][key] = None
                        else:
                            action_data["parameters"][key] = val

                    module_info["actions"].append(action_data)
            
            schema.append(module_info)

        return schema

registry = MarsRegistry()
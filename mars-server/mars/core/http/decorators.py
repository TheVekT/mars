from typing import Callable, Any, List, Union, Dict

def mars_module(name: str, prefix: str, compatibility: List[str] = None, requires_tools: List[str] = None):
    """Mark a class as a MARS HTTP module.

    The decorator attaches framework metadata to the class so the registry can
    discover the module name, base route, platform compatibility, and required
    external tools at runtime.
    """
    if compatibility is None: compatibility = ["windows"]
    if requires_tools is None: requires_tools = []
    
    def wrapper(cls):
        cls.__mars_module__ = True
        cls.__mars_name__ = name
        cls.__mars_prefix__ = prefix
        cls.__mars_compatibility__ = compatibility
        cls.__mars_requires_tools__ = requires_tools
        return cls
    return wrapper

def _base_endpoint(interaction_type: str, data_type: str, label: str, http_method: str, refresh_interval_ms: int = 0, **kwargs):
    """Attach endpoint metadata to a callable.

    This helper stores the common MARS endpoint attributes used by the router
    and UI schema generator. It does not wrap or modify the original callable
    behavior; it only annotates it with framework-specific metadata.
    """
    def wrapper(func: Callable):
        func.__mars_action__ = True
        func.__mars_interaction_type__ = interaction_type
        func.__mars_data_type__ = data_type
        func.__mars_label__ = label or func.__name__.replace("_", " ").capitalize()
        func.__mars_http_method__ = http_method.upper()
        func.__mars_refresh_interval__ = refresh_interval_ms
        func.__mars_params__ = kwargs
        return func
    return wrapper

def read_scalar(label: str = None, refresh_interval_ms: int = 0):
    """Mark an endpoint that returns a scalar value.

    Use this for endpoints that return a single value such as a number,
    status string, or short text that can be rendered as a compact UI block.
    """
    return _base_endpoint(interaction_type="read", data_type="scalar", label=label, http_method="GET", refresh_interval_ms=refresh_interval_ms)

def read_multiline(label: str = None, refresh_interval_ms: int = 0):
    """Mark an endpoint that returns multiline text.

    Use this for log viewers, console output, or any text payload that should
    be displayed in a scrollable multiline control.
    """
    return _base_endpoint(interaction_type="read", data_type="multiline", label=label, http_method="GET", refresh_interval_ms=refresh_interval_ms)

def read_dataset(columns: List[str], label: str = None, refresh_interval_ms: int = 0):
    """Mark an endpoint that returns a dataset.

    Use this for tabular data that should be rendered as a grid or table.
    The `columns` argument defines the expected column names for the UI.
    """
    return _base_endpoint(interaction_type="read", data_type="dataset", label=label, http_method="GET", refresh_interval_ms=refresh_interval_ms, columns=columns)

def execute_command(label: str = None, danger_level: str = "normal"):
    """Mark an endpoint that executes an action without input parameters.

    Use this for button-driven actions such as restarting a service, clearing
    a cache, or triggering an immediate command without additional form input.
    """
    return _base_endpoint(interaction_type="execute", data_type="none", label=label, http_method="POST", danger_level=danger_level)

def update_boolean(label: str = None, bind_source: Callable = None, bind_key: str = None):
    """Mark an endpoint that updates a boolean value.

    This is intended for toggle-style controls that change a boolean field or
    setting based on the current state returned by another endpoint.
    """
    return _base_endpoint(
        interaction_type="update", data_type="boolean", label=label, http_method="POST",
        bind_source=bind_source, bind_key=bind_key
    )

def update_range(min_val: Union[int, float, Callable], max_val: Union[int, float, Callable], 
                 label: str = None, bind_source: Callable = None, bind_key: str = None):
    """Mark an endpoint that updates a numeric range value.

    This is intended for slider-style controls where the UI needs minimum and
    maximum bounds to validate and render a numeric input.
    """
    return _base_endpoint(
        interaction_type="update", data_type="range", label=label, http_method="POST", 
        min_val=min_val, max_val=max_val, bind_source=bind_source, bind_key=bind_key
    )

def execute_with_params(params_schema: List[Dict[str, Any]], label: str = None, danger_level: str = "normal"):
    """Mark an endpoint that executes an action with input parameters.

    Use this for operations that require a dynamic form generated from a schema.

    Example params_schema for selecting an item from another endpoint:
    [
        {
            "name": "pid",
            "label": "Select a process",
            "type": "select_from_dataset",
            "source_endpoint": get_processes,
            "value_key": "PID",
            "display_key": "Name"
        }
    ]

    Example params_schema for a simple text input:
    [
        {
            "name": "process_name",
            "label": "Process name",
            "type": "input_string"
        }
    ]
    """
    return _base_endpoint(
        interaction_type="execute",
        data_type="parameterized",
        label=label,
        http_method="POST",
        danger_level=danger_level,
        params_schema=params_schema
    )
import functools
from fastapi import WebSocket

def with_ws_auth(endpoint_func):
    """Wrap a WebSocket endpoint with token-based authorization."""
    @functools.wraps(endpoint_func)
    async def wrapper(websocket: WebSocket, *args, **kwargs):
        expected_token = getattr(websocket.app.state, "auth_token", "")
        
        if expected_token:
            client_token = websocket.headers.get("X-MARS-Auth") or websocket.query_params.get("token")
            
            if client_token != expected_token:
                print(f"WebSocket connection denied for {websocket.url.path}: invalid token.")
                await websocket.close(code=1008, reason="Unauthorized")
                return

        return await endpoint_func(websocket, *args, **kwargs)
        
    return wrapper
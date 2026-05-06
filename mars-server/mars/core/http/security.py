from fastapi import Header, HTTPException, Request

async def verify_auth_token(request: Request, x_mars_auth: str = Header(None)):
    """Validate the X-MARS-Auth header against the server token."""
    expected_token = getattr(request.app.state, "auth_token", "")
    
    if not expected_token:
        return True

    if x_mars_auth != expected_token:
        raise HTTPException(
            status_code=401, 
            detail="Unauthorized: invalid password or missing X-MARS-Auth header"
        )
        
    return True
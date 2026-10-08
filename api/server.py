"""Canonical ASGI entry point used by Uvicorn and Unity."""

from api.main import app

__all__ = ["app"]

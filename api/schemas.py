"""Modelos de solicitud de la API; las respuestas siguen contratos JSON."""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, Field


class StartRequest(BaseModel):
    config: dict[str, Any] | None = Field(
        default=None,
        description="Configuracion completa opcional; si se omite se usa data/input_pet.json",
    )


class ActionResponse(BaseModel):
    status: str


# Visor WebGL de Unity

Carpeta pública del build WebGL. FastAPI la sirve en **`/unity/`** y el dashboard la incrusta en el panel **Vinculación con Unity**.

## Publicar un build

**Requisito:** WebGL Build Support para Unity 6000.3.25f1 — *Unity Hub → Installed → 6000.3.25f1 → Add modules → WebGL Build Support*. Sin ese módulo no existe `PlaybackEngines/WebGLSupport` y el build falla.

1. En Unity (`unity/PET_DigitalTwin_EditorInit`): **Tools → PET Digital Twin → Build WebGL**.
   Compila a una carpeta temporal, copia el resultado aquí y la limpia, de modo que el `README.md` de esta carpeta se conserva. Si el menú no aparece, pulsa **Ctrl+R** para recargar los scripts (`Assets/Editor/PETWebGLBuilder.cs`).
2. Debe quedar:

```
unity/webgl/
  index.html          ← obligatorio
  Build/
    *.wasm
    *.data
    *.framework.js
  TemplateData/
```

3. Reinicia la API y abre `http://127.0.0.1:8000/`. El panel detecta `/unity/index.html` automáticamente y muestra el visor.

Sin `index.html` la ruta responde 404 y el dashboard muestra el respaldo explicativo: es el comportamiento esperado mientras no hay build.

## Alternativa sin tocar la API

Si prefieres servir el build desde otro proceso:

```powershell
python -m http.server 8090 --directory ruta\al\build
```

y escribe `http://127.0.0.1:8090/index.html` en *Ruta del visor WebGL* del dashboard.

## Configuración

La carpeta se puede mover con la variable de entorno `PET_UNITY_WEBGL_DIR` (ruta absoluta o relativa a la raíz del proyecto). Si no existe, la API no monta `/unity` y sigue respondiendo igual.

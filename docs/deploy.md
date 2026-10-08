# Despliegue

Guía para dejar operativo el gemelo digital PET completo: simulación, API, dashboard y Unity.

Toda la cadena es:

```
data/input_pet.json  →  SimPy  →  results/  →  FastAPI  →  contracts/  →  Unity
                                                  ↓
                              dashboard/dist/index.html  ·  /unity/ (build WebGL)
```

## Despliegue rápido

```powershell
deploy.bat
```

En un clon nuevo, `deploy.bat` crea `.venv` automáticamente e instala las dependencias con `pip install -e .` antes de arrancar el sistema.

Equivalente desde la terminal, tras `python -m pip install -e ".[test]"`:

```powershell
pet-deploy
```

El lanzador:

1. Comprueba `simpy`, `fastapi` y `uvicorn`; desde `deploy.bat` se instalan automáticamente si faltan.
2. Ejecuta la simulación **solo** si falta algún archivo de `results/` o si es anterior a `data/input_pet.json`.
3. Si el puerto ya lo ocupa una API propia, la reutiliza; si lo ocupa otro proceso, lo indica con su PID y sugiere otro puerto.
4. Arranca Uvicorn, espera a que `/dashboard` responda, abre el navegador y reporta API, resultados y visor WebGL.

| Opción | Efecto |
|---|---|
| `--port 8010` | Puerto distinto al 8000 |
| `--force-sim` | Reejecuta la simulación aunque los resultados estén vigentes |
| `--skip-sim` | No ejecuta SimPy, sirve `results/` tal cual |
| `--restart` | Detiene y relanza una API propia que ya esté corriendo |
| `--no-browser` | No abre el navegador |
| `--config` / `--results` | Input y directorio de resultados alternativos |

`deploy.bat` y `pet-deploy` no pueden iniciar Unity (el repositorio no garantiza soporte CLI del Editor); dejan escrita la ruta de `unity/PET_DigitalTwin_EditorInit`.

## Comprobación previa

```powershell
cd C:\Users\test123\Projects\PET_Digital_Twin
.\.venv\Scripts\Activate.ps1
python -m pip install -e ".[test]"
pytest
```

Salida esperada: `30 passed`.

## 1. Simulación

```powershell
python -m simulation.main --config data/input_pet.json --output results
```

Genera en `results/`:

| Archivo | Contenido |
|---|---|
| `events.csv` | Transiciones de proceso |
| `machine_states.csv` | Instantáneas de máquina y longitud de cola |
| `kpis.json` | Throughput, lead time, WIP, scrap, utilización, cuello de botella |

Comparación de capacidad y sensibilidad (opcional):

```powershell
python -m simulation.scenarios --config data/input_pet.json --demand 100000
```

Escribe `results/scenario_comparison.csv`.

## 2. API y dashboard

```powershell
uvicorn api.server:app --reload --port 8000
```

`api/server.py` reexporta `api.main:app`. Al arrancar carga el resultado más reciente de `results/`; si no es anterior a `data/input_pet.json`, el estado queda en `FINISHED`.

Abre **http://127.0.0.1:8000/**. `/` redirige a `/dashboard`.

| Ruta | Respuesta |
|---|---|
| `GET /` | 307 → `/dashboard` |
| `GET /dashboard` | Panel HTML (`dashboard/dist/index.html`) |
| `GET /config` | Configuración de la simulación |
| `GET /status` | Estado, tiempo simulado, unidades, demanda |
| `GET /machines` | Lista de estados de máquina |
| `GET /machines/{id}` | Estado de una máquina |
| `GET /kpis` | Indicadores (`contracts/kpis.schema.json`) |
| `GET /simulation/time` | Tiempo simulado y unidad |
| `POST /simulation/start` \| `/pause` \| `/reset` | Control de corrida en segundo plano |
| `GET /unity/…` | Build WebGL de Unity (si existe `unity/webgl/`) |

## 3. Unity (escritorio)

> Hay dos carpetas en `unity/` y solo una es un proyecto operativo.
>
> | Carpeta | Estado |
> |---|---|
> | `unity/PET_DigitalTwin_EditorInit` | **Usar esta.** `ProjectSettings/` completo, escena `Assets/Scenes/PET_Plant.unity` construida y registrada en `EditorBuildSettings`, `api_config.json` presente, Unity 6000.3.25f1 |
> | `unity/PET_DigitalTwin` | Sin inicializar: `ProjectSettings/` y `Assets/Scenes/` vacíos. Solo contiene los scripts con namespaces y la guía manual `SCENE_SETUP.md` |

Orden de arranque:

1. Levanta la API (paso 2). Unity consume `127.0.0.1:8000`.
2. Abre `unity/PET_DigitalTwin_EditorInit` desde Unity Hub.
3. Abre `Assets/Scenes/PET_Plant.unity`.
4. Pulsa **Play**.

Señales de que funciona: desaparece el banner offline, los nueve equipos cambian de color según estado, `IMM-101` anima el molde y las colas crecen y se vacían.

Para reconstruir la planta desde cero: **Tools → PET Digital Twin → Build PET Plant**.

IDs válidos, idénticos en Python, dashboard y Unity:

`D-101` `H-101` `IMM-101` `CV-101` `BF-101` `OV-101` `SBM-101` `QC-101` `PKG-101`

## 4. Visor WebGL

`api/main.py` monta `unity/webgl/` en **`/unity/`** cuando la carpeta existe. Sin build, `/unity/index.html` responde 404 y el dashboard muestra el respaldo explicativo: es el comportamiento esperado. Como la carpeta ya existe, el mount está activo: basta con dejar el `index.html`, **no hay que reiniciar la API**.

### Generar el build

Requisito: **WebGL Build Support** para Unity 6000.3.25f1. *Unity Hub → Installed → 6000.3.25f1 → Add modules → WebGL Build Support*. Si no está, `PlaybackEngines/WebGLSupport` no existe y el build falla.

1. En el Editor del proyecto `PET_DigitalTwin_EditorInit`: **Tools → PET Digital Twin → Build WebGL**.
2. Compila a una carpeta temporal, copia `index.html`, `Build/` y `TemplateData/` a `unity/webgl/` (conserva el README) y limpia el temporal.
3. Abre `http://127.0.0.1:8000/`: el panel detecta `/unity/index.html` en el siguiente sondeo.

Si el menú no aparece, pulsa **Ctrl+R** en el Editor para que recargue los scripts (`Assets/Editor/PETWebGLBuilder.cs`).

Alternativa manual: **File → Build Settings → WebGL → Build** y copiar el contenido de la carpeta de salida a `unity/webgl/`.

### Servir el build desde otro proceso

```powershell
python -m http.server 8090 --directory ruta\al\build
```

y escribe `http://127.0.0.1:8090/index.html` en *Ruta del visor WebGL* del dashboard.

La carpeta se puede mover con `PET_UNITY_WEBGL_DIR`.

## 5. Verificación cruzada

```powershell
Invoke-RestMethod http://127.0.0.1:8000/status
Invoke-RestMethod http://127.0.0.1:8000/machines
Invoke-RestMethod http://127.0.0.1:8000/kpis
```

En el dashboard, el panel **Vinculación con Unity** diagnostica el enlace:

| Indicador | Significado |
|---|---|
| Backend compartido | Latencia de los endpoints que también consulta Unity |
| Punto de enlace Unity | Compara `Assets/Resources/Config/api_config.json` con el origen de la página |
| Visor 3D Unity | Disponibilidad del build WebGL |
| Sincronización | Si el flujo cambia en cada lectura (`En vivo`, `pausa`, `Corrida publicada`) |
| Compatibilidad de escena | IDs publicados frente al layout fijo de `PlantManager.cs` |

Si todo está enlazado, el indicador global muestra **`Vinculación activa`**.

## Variables de entorno

| Variable | Por defecto | Efecto |
|---|---|---|
| `PET_CONFIG_PATH` | `data/input_pet.json` | Entrada de la simulación |
| `PET_RESULTS_DIR` | `results` | Salidas |
| `PET_UNITY_WEBGL_DIR` | `unity/webgl` | Carpeta publicada en `/unity/` |
| `PET_STEP_DELAY_SECONDS` | `0.02` | Retraso de cada paso en corridas en segundo plano |

## Problemas conocidos

| Síntoma | Causa | Solución |
|---|---|---|
| 404 en `/` o `/dashboard` | Proceso anterior en el puerto, sin esas rutas | `Stop-Process -Id <PID> -Force` y reiniciar, o `pet-deploy --restart` |
| `address already in use` | Puerto 8000 ocupado | `pet-deploy --port 8010`, o liberar el puerto |
| `Faltan dependencias` al lanzar | Falta `pip install -e ".[test]"` | Instalar con el mismo intérprete del `.venv` |
| La simulación se reejecuta siempre | `results/` anterior a `data/input_pet.json` | Regenerar una vez, o usar `--skip-sim` |
| Aviso de resultados nuevos con API antigua | La API en marcha publica la corrida anterior | `pet-deploy --restart` |
| Unity no recibe datos | API no levantada o host distinto | API primero; revisar `api_config.json` |
| Visor "No disponible" | No hay build en `unity/webgl/` | Paso 4: **Tools → PET Digital Twin → Build WebGL** |
| Escena vacía | Se abrió `PET_DigitalTwin` | Usar `PET_DigitalTwin_EditorInit` |
| IDs `IMM-102`, `SBM-102` marcados | Configuración con varias inyectoras/sopladoras; Unity tiene layout fijo de nueve | Ampliar `MachineLayout` en `PlantManager.cs` |
| `/unity/` no responde | La carpeta no existía al arrancar | Crearla y reiniciar la API |
| Build WebGL fallido o el menú no aparece | Falta **WebGL Build Support**, o los scripts no se recargaron | Unity Hub → Installed → `6000.3.25f1` → Add modules; después Ctrl+R en el Editor |

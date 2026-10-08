# PET Digital Twin

Gemelo digital discreto de una línea de envases PET. SimPy ejecuta la corrida, FastAPI publica el último resultado y Unity lo consume usando el mismo contrato JSON.

La cadena integrada es:

`data/input_pet.json → SimPy → results/ → FastAPI → contracts/ → Unity`

Para dejarlo operativo de una vez después de descargar/clonar, doble clic en `deploy.bat`. El script crea `.venv`, instala dependencias, ejecuta la simulación si hace falta, arranca la API y abre el dashboard. Los detalles están en la sección DESPLIEGUE AUTOMÁTICO.

Los únicos IDs de equipo válidos son `D-101`, `H-101`, `IMM-101`, `CV-101`, `BF-101`, `OV-101`, `SBM-101`, `QC-101` y `PKG-101`. Todos los tiempos internos y publicados están en segundos; temperatura en °C; presión en bar; masa interna en kg; `progress` es una fracción de 0 a 1 y los porcentajes KPI van de 0 a 100.

## PREREQUISITOS

- Windows 10/11 con PowerShell (los mismos comandos funcionan en una pestaña PowerShell de Warp).
- Python 3.11 o superior.
- Unity Hub y Unity Editor 2022.3 LTS o superior para la visualización.
- No se requiere una base de datos ni servicios externos.

El repositorio no contiene todavía una escena Unity serializada ni archivos de versión completos del Editor. Por ello Unity debe inicializar el proyecto y la escena una vez desde el Editor; no se presupone soporte CLI de Unity.

## INSTALACIÓN

Desde la raíz del repositorio:

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -e ".[test]"
```

Si PowerShell bloquea la activación, se puede invocar directamente `\.venv\Scripts\python.exe` o habilitar scripts solo para la sesión con `Set-ExecutionPolicy -Scope Process Bypass`.

Verificación:

```powershell
pytest
```

## EJECUCIÓN DESDE WARP

Abrir dos pestañas PowerShell en la raíz del repositorio y activar `.venv` en ambas.

Terminal 1:

```powershell
python -m simulation.main
```

Terminal 2, después de que termine la simulación:

```powershell
uvicorn api.server:app --reload
```

La API carga `results/machine_states.csv` y `results/kpis.json` si ambos son más recientes que el input. Así Unity observa exactamente la corrida creada en la Terminal 1. Comprobarla con:

```powershell
Invoke-RestMethod http://127.0.0.1:8000/status
Invoke-RestMethod http://127.0.0.1:8000/machines
Invoke-RestMethod http://127.0.0.1:8000/kpis
```

## EJECUCIÓN DE SIMPY

Ejecución estándar:

```powershell
python -m simulation.main
```

Rutas explícitas:

```powershell
python -m simulation.main --config data/input_pet.json --output results
```

La semilla `random_seed` hace reproducible la corrida. `numero_maquinas_inyeccion` y `numero_maquinas_soplado` representan la capacidad paralela de las estaciones oficiales `IMM-101` y `SBM-101`; no generan IDs no autorizados.

## DESPLIEGUE AUTOMÁTICO

Un solo comando deja operativo el sistema completo: crea `.venv` si no existe, instala dependencias, ejecuta la simulación si `results/` falta o es anterior al input, arranca la API, abre el navegador y reporta el estado del visor WebGL.

Doble clic en Windows:

```powershell
deploy.bat
```

O desde la terminal (tras `pip install -e .`):

```powershell
pet-deploy
```

Si la API propia ya está corriendo se reutiliza en lugar de duplicarla; si el puerto lo ocupa otro proceso distinto, lo indica con su PID. Unity no se puede iniciar por CLI desde este repositorio, así que el lanzador deja escrita la ruta exacta de `PET_DigitalTwin_EditorInit`.

| Opción | Efecto |
|---|---|
| `--port 8010` | Puerto distinto al 8000 |
| `--force-sim` | Reejecuta la simulación aunque los resultados estén vigentes |
| `--skip-sim` | No ejecuta SimPy, sirve `results/` tal cual |
| `--restart` | Detiene y relanza una API propia que ya esté corriendo |
| `--no-browser` | No abre el navegador |
| `--config` / `--results` | Input y directorio de resultados alternativos |

## EJECUCIÓN API

```powershell
uvicorn api.server:app --reload
```

Endpoints de lectura:

- `GET /status`: estado, tiempo simulado en segundos, unidades terminadas y demanda.
- `GET /machines`: array con los nueve estados conformes a `contracts/machine_state.schema.json`.
- `GET /machines/{machine_id}`: una máquina oficial.
- `GET /kpis`: indicadores conformes a `contracts/kpis.schema.json`.
- `GET /simulation/time`: tiempo simulado y unidad.
- `GET /` y `GET /dashboard`: panel visual (`dashboard/dist/index.html`).
- `GET /unity/…`: build WebGL de Unity servido desde `unity/webgl/` (si la carpeta existe).

Control opcional de una corrida en segundo plano: `POST /simulation/start`, `POST /simulation/pause` y `POST /simulation/reset`. La ruta ideal SimPy-primero no necesita estos POST.

Para publicar otro input/directorio o mover el build WebGL, definir las variables antes de iniciar Uvicorn:

```powershell
$env:PET_CONFIG_PATH = "data/scenarios/high_demand.json"
$env:PET_RESULTS_DIR = "results/high_demand"
$env:PET_UNITY_WEBGL_DIR = "unity/webgl"
uvicorn api.server:app --reload
```

El despliegue completo paso a paso está en `docs/deploy.md`.

## DASHBOARD Y VINCULACIÓN UNITY

`uvicorn api.server:app` publica el panel visual en `http://127.0.0.1:8000/`, que redirige a `/dashboard` (`dashboard/dist/index.html`). Es la misma plataforma visual que consume la API, sin build ni dependencias adicionales.

Junto a los KPIs, la tabla de máquinas y el control de corrida, la sección **Vinculación con Unity** enlaza el modelo 3D con el panel:

- **Backend compartido**: estado y latencia del mismo `/status`, `/machines` y `/kpis` que consulta Unity.
- **Punto de enlace Unity**: compara el host/puerto configurado en Unity (`Assets/Resources/Config/api_config.json`, por defecto `http://127.0.0.1:8000`) con el origen de la página. Si difieren, Unity y el panel no leen la misma API.
- **Visor 3D Unity**: incrusta el build WebGL de Unity en un `iframe`. Sondea la ruta configurada y la carga solo si devuelve `text/html`; si no, explica el motivo. La ruta y el destino se editan en el panel y quedan en el navegador. FastAPI publica `unity/webgl/` en `/unity/` (esa es la ruta por defecto); se genera con **Tools → PET Digital Twin → Build WebGL** desde `PET_DigitalTwin_EditorInit`, y como la carpeta ya existe basta con dejar el `index.html` ahí, sin reiniciar. Alternativamente puede servirse con `python -m http.server` e indicar su URL. Requiere **WebGL Build Support** (Unity Hub → Add modules).
- **Sincronización de datos**: indica si el flujo cambia en cada lectura (`En vivo`, `pausa`, `Corrida publicada` o `Sin cambios`).
- **Compatibilidad de escena**: contrasta los IDs publicados con los nueve del layout fijo de `PlantManager.cs` y señala IDs extra (`IMM-102`, `SBM-102`, …) o faltantes, con la acción correctiva.
- **Control de simulación**: iniciar, pausar y reiniciar desde el propio panel.

## EJECUCIÓN UNITY

> El repositorio contiene dos carpetas en `unity/`:
>
> - **`PET_DigitalTwin_EditorInit`** — proyecto inicializado: `ProjectSettings/` completo, escena `Assets/Scenes/PET_Plant.unity` ya construida y registrada en `EditorBuildSettings`, y `api_config.json`. **Es la que hay que abrir para ejecutar.** El menú `Tools → PET Digital Twin → Build PET Plant` reconstruye la planta.
> - **`PET_DigitalTwin`** — scripts con namespaces y `SCENE_SETUP.md`, pero sin `ProjectSettings` ni escena: requiere montaje manual con los pasos siguientes.

No se ejecuta Unity por línea de comandos porque el repositorio no incluye soporte CLI garantizado del Editor. Preparación exacta en Unity Editor:

1. En Unity Hub, agregar/abrir `unity/PET_DigitalTwin` con Unity 2022.3 LTS o superior. Permitir que el Editor inicialice `ProjectSettings`.
2. Crear una escena 3D y guardarla como `Assets/Scenes/MainScene.unity`.
3. Crear un GameObject `Managers` y agregar `PlantManager` y `DigitalTwinApiClient`.
4. En `PlantManager`, asignar `Api Client` al componente del mismo objeto. El script construye automáticamente los nueve equipos oficiales; no se deben crear IDs adicionales.
5. Verificar en `DigitalTwinApiClient`: `host=127.0.0.1`, `port=8000`, `pollIntervalMs=200`. Alternativamente editar `Assets/Resources/Config/api_config.json`.
6. Configurar cámara, HUD, prefabs y referencias del Inspector siguiendo `unity/PET_DigitalTwin/SCENE_SETUP.md`.
7. Con FastAPI activo y `/status` accesible, pulsar **Play**. Azul significa `FINISHED`, verde `RUNNING`, ámbar `WAITING`, rojo `FAULT`, naranja `MAINTENANCE` y gris `IDLE`.

`DigitalTwinApiClient` adapta el array superior de `/machines` a la limitación de `JsonUtility` y convierte telemetría JSON `null` a `-1` solo dentro de Unity. Los nombres de los campos consumidos son los mismos de FastAPI y del contrato.

## CAMBIO DE INPUTS

Editar `data/input_pet.json`. Las unidades aceptadas están declaradas en `contracts/simulation_input.schema.json`:

- tiempos: `seconds`, `minutes` u `hours`;
- masa de pieza: `grams` o `kg`;
- capacidad del secador: `kg`;
- `scrap_*`: fracción entre 0 y 1;
- `MTBF` y `MTTR`: horas.

Después de cambiar el input, volver a ejecutar SimPy antes de arrancar/reiniciar la API. La API rechaza automáticamente resultados cuya fecha sea anterior al input y, en ese caso, publica estado `READY` en lugar de datos obsoletos.

## ESCENARIOS WHAT-IF

Guardar cada variante fuera del input base, por ejemplo `data/scenarios/high_demand.json`, y separar sus resultados:

```powershell
python -m simulation.main --config data/scenarios/high_demand.json --output results/high_demand
$env:PET_CONFIG_PATH = "data/scenarios/high_demand.json"
$env:PET_RESULTS_DIR = "results/high_demand"
uvicorn api.server:app --reload
```

Variables típicas: demanda, capacidad paralela de inyección/soplado, capacidad de buffer, distribuciones de ciclo, scrap, MTBF y MTTR. Mantener la misma `random_seed` al comparar escenarios para aislar el efecto del cambio.

## RESULTADOS

Cada corrida completa crea:

- `results/events.csv`: trazabilidad de esperas, procesos, fallas, reparaciones y rechazos.
- `results/machine_states.csv`: historial de estados de las nueve máquinas.
- `results/kpis.json`: producción total/buena, rechazo, throughput, utilización y disponibilidad por máquina, colas, lead time, WIP, cumplimiento y cuello de botella.

El análisis what-if opcional crea además `results/scenario_comparison.csv` (o la ruta indicada al ejecutar escenarios).

Los tests validan el input, las respuestas API y los KPIs contra `contracts/`, además de invariantes físicos: balance de producción, rechazo por etapa, porcentajes entre 0 y 100, WIP acotado por demanda, lead time no superior a la corrida y ausencia de magnitudes negativas.

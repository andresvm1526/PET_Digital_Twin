# Arquitectura del gemelo digital PET

## Alcance

El sistema implementa una simulación de eventos discretos académica e industrial de la orden completa. El tiempo canónico es el segundo y la semilla configurable permite repetir experimentos. La integración con ANSYS GRANTA es deliberadamente desacoplada: las propiedades conocidas pueden almacenarse en `material.propiedades_granta`; tiempos, capacidades, scrap y confiabilidad siguen siendo entradas configurables.

## Flujo y entidades

1. Una orden se divide en `PETLot` según la capacidad másica del secador.
2. Al finalizar el secado, cada lote genera `Preform` individuales.
3. Las preformas atraviesan inyección, enfriamiento, buffer con capacidad finita y recalentamiento.
4. Después del recalentamiento pasan a `PETBottle` y recorren soplado, calidad y empaque.
5. Los rechazos son terminales en inyección, soplado o calidad.

El buffer mantiene ocupada una plaza hasta que el horno acepta la preforma. Esto reproduce bloqueo aguas arriba y acumulación real de WIP. Los valores `numero_maquinas_inyeccion` y `numero_maquinas_soplado` se modelan como capacidad paralela interna de las estaciones oficiales `IMM-101` y `SBM-101`; no crean IDs adicionales.

## Fallas y estados

El tiempo entre fallas se muestrea exponencialmente usando MTBF. Una falla interrumpe la operación, conserva el recurso y añade una reparación exponencial con media MTTR. Los estados publicados son `IDLE`, `WAITING`, `RUNNING`, `FAULT`, `MAINTENANCE` y `FINISHED`.

Los CSV constituyen una bitácora reproducible. `events.csv` guarda transiciones del proceso y `machine_states.csv` conserva las instantáneas necesarias para reconstruir estados y longitud de cola a lo largo del tiempo.

## Fronteras de módulos

- `simulation/config.py`: validación y unidades.
- `simulation/entities.py`: entidades de dominio.
- `simulation/machine.py`: recursos y acumuladores.
- `simulation/model.py`: flujo SimPy y trazabilidad.
- `simulation/kpis.py`: indicadores derivados.
- `api/manager.py`: ejecución concurrente, pausa y reset.
- `api/main.py`: interfaz HTTP para Unity.
- `contracts/`: esquemas independientes del lenguaje consumidor.

## Supuestos explícitos

- `masa_pieza` representa la masa de la preforma/botella antes de scrap.
- `demanda` es el número bruto liberado a producción; por ello el cumplimiento puede ser menor a 100 % si hay rechazo.
- MTBF y MTTR se expresan en horas.
- La disponibilidad es `(tiempo total - parada) / tiempo total`; la utilización usa solo tiempo productivo.
- El cuello de botella inicial es el tipo de recurso con mayor utilización promedio. La cola promedio se reporta como evidencia complementaria.

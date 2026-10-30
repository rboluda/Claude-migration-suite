# export-synthetic

Export SINTÉTICO escrito a mano, sin datos de nadie. Sigue la estructura conocida públicamente del export y sirve para probar el parser y el `.clo` mientras no haya fixtures anonimizadas de un export real (M0).

Cubre: chat sin padres (árbol inferido), chat con padres y una rama por edición, adjunto con texto (`.pdf` y `.txt`), imagen solo referenciada, campos desconocidos que deben conservarse y un archivo del ZIP que el parser no entiende.

Cuando M0 documente el esquema real en `docs/export-schema.md`, sustituir o completar con fixtures derivadas de un export real anonimizado.

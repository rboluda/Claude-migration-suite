# Migrador

Aplicación de escritorio para Windows que guarda **todo** el contenido de una cuenta de claude.ai en un único archivo `.clo`, sin resumir ni perder nada, permite explorarlo y prepara lo que elijas para llevarlo a otra cuenta.

*English below.*

> Estado: en desarrollo (0.x). El lector del export es provisional hasta validarlo con exports reales.

## Qué hace

- **Importa el export de datos** de la cuenta (el `.zip` que se pide en Ajustes → Privacidad → Exportar datos) y lo guarda en un `.clo` junto con el export original, intacto.
- **Conserva los chats como árboles**: las ediciones y regeneraciones son ramas, y se puede navegar por cada versión.
- **Indica la fidelidad de cada elemento** (original, equivalente, texto extraído, reconstrucción, perdido) y detalla cada pérdida: qué era, dónde estaba, qué queda y qué mensajes posteriores lo mencionan.
- **Copia la carpeta de Claude Code** (`%USERPROFILE%\.claude`: instrucciones, ajustes, skills, agentes, historial y memoria), **sin credenciales ni tokens**.
- Interfaz en **español e inglés**.

## Descarga

Descarga `Migrador-x.y.z.exe` de la [última versión](https://github.com/rboluda/Claude-migration-suite/releases/latest) y ábrelo. Es un único archivo: no hay que instalar nada más.

## Privacidad

Todo se procesa en tu equipo. Sin telemetría ni servidores propios.

La única conexión que hace Migrador es para las actualizaciones: como mucho una vez al día (o con *Ayuda → Buscar actualizaciones*) consulta la API pública de GitHub (`api.github.com/repos/rboluda/Claude-migration-suite/releases/latest`) y, si hay una versión nueva, descarga el ejecutable y su `SHA256SUMS.txt` desde GitHub. Antes de instalarla comprueba el SHA-256 y que el archivo sea Migrador en esa versión. Estas peticiones no llevan nada tuyo más allá de lo que lleva cualquier petición HTTPS (la IP y un `User-Agent` con la versión). Las dos opciones (buscar e instalar sin preguntar) se pueden desactivar en *Archivo → Ajustes*.

## Compilar

Requiere el SDK de .NET 10.

```
dotnet test tests/Migrador.Tests
dotnet publish src/Migrador.App -c Release -o publish
```

## Autor

[rboluda](https://github.com/rboluda)

---

## English

Windows desktop app that saves **all** the content of a claude.ai account in a single `.clo` file, without summarizing or losing anything, lets you explore it, and prepares what you choose to take to another account.

- Imports the account's data export and keeps the original export untouched inside the `.clo`.
- Keeps chats as trees (edits and regenerations are branches).
- Shows the fidelity of every item and details every loss.
- Copies the Claude Code folder (`%USERPROFILE%\.claude`) without credentials or tokens.
- Spanish and English interface.

**Privacy:** everything is processed locally, no telemetry. The only connection is the update check against the public GitHub API (at most once a day), and the download of new versions, verified by SHA-256. Both can be turned off in *File → Settings*.

**Download:** `Migrador-x.y.z.exe` from the [latest release](https://github.com/rboluda/Claude-migration-suite/releases/latest).

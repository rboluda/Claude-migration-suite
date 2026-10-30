# Cambios

## 0.1.0 — 2026-10-30

Primera versión.

- **Importar el export de datos** de la cuenta y guardarlo en un archivo `.clo` con el export original intacto, cada chat, cada proyecto y la huella SHA-256 de todo el contenido. El `.clo` se escribe de forma segura y se comprueba al guardarlo.
- **Lector con ramas:** los chats se conservan como árboles; las ediciones y regeneraciones son versiones que se pueden recorrer. Muestra también el razonamiento y las llamadas a herramientas.
- **Fidelidad de cada elemento** (original, equivalente, texto extraído, reconstrucción, perdido) y sección **Pérdidas** con el detalle de cada una y los mensajes posteriores que la mencionan.
- **Búsqueda** en todo el texto sin distinguir tildes ni mayúsculas.
- **Proyectos** con sus instrucciones y documentos.
- **Claude Code:** copia de la carpeta `.claude` de este equipo sin credenciales, tokens ni cachés, con la ruta real de cada proyecto y su remoto git, y aviso del borrado automático del historial.
- **Español e inglés.**
- **Actualización desatendida** desde GitHub Releases, con comprobación de SHA-256 y de versión.
- Lector del export **provisional**, pendiente de validar con exports reales.

---

# Changelog

## 0.1.0 — 2026-10-30

First release: import of the account data export into a verified `.clo` package (original export kept untouched), chat reader with branches, per-item fidelity and a Losses section with impact, accent-insensitive search, projects, a credential-free copy of the Claude Code folder, Spanish and English interface, and unattended updates from GitHub Releases verified by SHA-256. The export reader is provisional until validated against real exports.

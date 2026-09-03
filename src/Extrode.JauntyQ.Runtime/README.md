# Extrode.JauntyQ.Runtime

This project is intentionally (near-)empty.

JauntyQ generated code carries everything it needs inline: parameter binding, connection open/close, ordinal-typed reader access, shape guard, async twins. There is no shared helper the generator calls at runtime — each emitted method is self-contained ADO.NET.

The project is kept in the solution as a deployment surface and is reserved for future opt-in helpers (e.g., transaction support, connection-pool wrappers) that consumers can choose to depend on. Do not add helpers here that generated code calls unconditionally; that would reintroduce the boxing and indirection the generator was designed to avoid.

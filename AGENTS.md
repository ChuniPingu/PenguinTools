# PenguinTools guidance

- Keep credentials and private implementation details out of public source and artifacts. Public documentation must not link to private repositories.
- Use `MessageDescriptor`, `Msg`, and `MsgKeys` for diagnostics; update both catalogs in `docs/locales/` when adding message keys.
- Keep proprietary samples local. Optional fixture tests may skip without samples; report missing coverage.
- `External/` contains separate repositories. Change their source or submodule references only when the task needs it.

## Documentation

- Keep READMEs to overview, prerequisites, quick start, common commands, and links. Keep tool and library versions in configuration.
- Put architecture, implementation, workflow, deployment, and format details in `docs/`; update the relevant document with a feature change.
- Preserve license text and source attribution in notices and format references.

Use [architecture](docs/architecture.md) for project boundaries and CLI contracts, [development](docs/development.md) for native builds or releases, [testing](docs/testing.md) for local fixtures, and [format references](docs/formats/README.md) for chart syntax.

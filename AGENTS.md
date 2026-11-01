# PenguinTools guidance

- Keep credentials, proprietary samples, and private implementation details out of public source and artifacts. Public documentation must not link to private repositories.
- Use `MessageDescriptor`, `Msg`, and `MsgKeys` for diagnostics; update both catalogs in `locales/` when adding message keys.
- Optional fixture tests may skip without local samples; report missing coverage.
- `External/` contains separate repositories. Change their source or submodule references only when the task needs it.
- Use Conventional Commits.

## Documentation

- Update docs when usage, public contracts, prerequisites, or lasting architecture constraints change. Revise existing passages; keep one primary location per fact. Put investigation and validation notes in PRs or commits.
- Keep READMEs to overview, prerequisites, quick start, common commands, and links. Tool and library versions belong in configuration.

Use [architecture](docs/architecture.md) for project boundaries, [development](docs/development.md) for builds and releases, and [testing](docs/testing.md) for fixtures and test setup.

## C# changes

Prefer modern, stable C# supported by the affected project's SDK, target framework,
and effective `LangVersion`.
Check [global.json](global.json), [Common.props](Common.props), and the affected project and its imports when choosing syntax.
Keep modernization within the requested change.

Prefer file-scoped namespaces, target-typed `new`, collection expressions, primary
constructors, patterns, switch expressions, using declarations, raw strings, and
supported `field` or extension blocks where they improve clarity. Preserve collection
and disposal semantics, framework behavior, and binding, source-generator,
serialization, and public API contracts. Explain meaningful compatibility exceptions.

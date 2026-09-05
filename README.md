# FileFlow CLI

A local, root-scoped file manager with an interactive shell and script mode. It previews and journals file mutations, supports conflict-aware undo, and finds duplicate content without modifying it.

## Features

- Supports `connect`, `disconnect`, `pwd`, `ls`, `cd`, `show`, `copy`, `move`, `rename`, `delete`, `tree`, `help`, and `exit`.
- Builds an immutable operation plan before every copy, move, rename, or delete, then validates and previews it before any workspace mutation.
- Supports valueless `--dry-run`, local `history`, and `undo <transaction-id>` for all four mutation types.
- Moves deleted files into per-transaction application-data trash instead of deleting them immediately.
- Finds duplicate files with `duplicates <path>` by grouping on size before streaming SHA-256 candidates.
- Handles relative, absolute, and quoted paths including spaces.
- Rejects unknown flags, extra arguments, missing flag values, and unclosed quotes.
- Preserves the current directory when navigation fails and lexically rejects paths outside the connected root.
- Refuses overwrites, same-path mutations, changed-file undo, unsafe broad roots, and mutation paths containing symbolic links/reparse points.
- Renders listings, trees, duplicate groups, and paths with deterministic case-insensitive plus ordinal tie-break ordering.

## Tech Stack

C# · .NET 9 · System.IO · xUnit

## Architecture

`FileFlow.Core` contains the command abstraction, session, local file-system adapter, immutable operation plans, validator/executor, JSON-lines journal, undo logic, duplicate scanner, tree walker, and Visitor. `FileFlow.Cli` contains the tokenizer, Builder, Chain of Responsibility parser, testable shell, and console host.

The mutation path is explicit:

```text
parse -> plan -> validate -> preview -> journal prepared -> execute -> journal completed
undo -> validate -> journal undo-prepared -> reverse mutation -> journal undone
```

Dry-run stops after preview. A failed prepared-journal append causes no workspace mutation. Undo reloads a completed journal entry, checks path occupancy, link components, canonical delete-trash location, and the recorded SHA-256 fingerprint. It records `undoPrepared` before reversing the mutation and appends `undone` afterward. A prepared-only or undo-prepared-only entry is recovery evidence and is never automatically retried.

## Project Structure

- `src/FileFlow.Core` — file operations, journal/undo, duplicate analysis, and session rules.
- `src/FileFlow.Cli` — parser and executable shell.
- `tests/FileFlow.UnitTests` — tokenizer, parser, and path-boundary tests.
- `tests/FileFlow.IntegrationTests` — real `LocalFileSystem` scenarios in isolated temporary directories.
- `examples/commands.txt` — safe read-only example session.

## Getting Started

Requires the .NET 9 SDK.

Journal and trash data default to the platform `LocalApplicationData/FileFlow` directory, never the repository or connected workspace. The shell also accepts an absolute application-data root through its constructor; all tests inject isolated temporary workspace and state directories.

## Build

```bash
dotnet build FileFlow.slnx -c Release
```

## Run

Interactive mode:

```bash
dotnet run --project src/FileFlow.Cli
```

Script mode from the workspace root:

```bash
dotnet run --project src/FileFlow.Cli -- --script examples/commands.txt
```

Run `help` in the shell or pass `--help` for command syntax.

## Tests

```bash
dotnet test FileFlow.slnx -c Release
```

## Examples

```text
connect "/tmp/example workspace"
pwd
show "notes/my file.txt"
copy "notes/my file.txt" backup.txt
copy "notes/my file.txt" backup-preview.txt --dry-run
history
undo 00000000-0000-0000-0000-000000000000
duplicates . --format json
tree --depth 2
exit
```

Replace the zero transaction ID in the example with an ID printed by a completed mutation or by `history`. Duplicate output supports `text` (default) and `json`. Duplicate scanning is read-only: files are grouped by byte length first, and only same-size candidates are hashed with streaming SHA-256.

Depth `0` prints only the current root, depth `1` includes its direct children, and larger values recurse accordingly.

## Design Decisions

The session treats the connected directory as a lexical navigation boundary, not as a security sandbox. Mutation validation additionally rejects file-system roots, the exact home directory, application-data roots, state/trash paths, and existing symbolic-link/reparse-point components. Commands depend on `IFileSystem`, while `LocalFileSystem` is the production adapter. Expected command errors are reported without terminating an interactive session. The CLI project is configured with `PackAsTool` for optional local packaging, but no package is published.

No implicit overwrite or force mode exists. Undo also refuses to proceed when the reverse destination is occupied or current content no longer matches the recorded fingerprint. The injected journal service defaults to append-only JSON Lines with transaction ID, UTC timestamp, operation, source, destination, trash path, fingerprint, and `prepared`, `completed`, `undoPrepared`, or `undone` status.

## Limitations / Future Improvements

FileFlow operates only on local files and intentionally refuses overwrites. Read-only navigation and `show` still follow operating-system symbolic-link semantics, so the connected root must not be treated as an access-control boundary; mutation and undo revalidate detected links, but this is not an atomic OS sandbox against concurrent replacement. Trash moves require the workspace and application-data root to support an ordinary file move; cross-volume trash is not implemented. Journal synchronization is process-local, JSONL appends are not fsynced or transactional, and a crash or completion-append failure can leave a prepared-only entry after a mutation; recovery is deliberately manual. Trash retention is also manual. Globbing and batch mutation are intentionally unsupported; run explicit single-file commands instead.

# dotnet-solution-items CLI Plan

## Status

Planned.

## Purpose

Create a .NET global tool named `dotnet-solution-items` that keeps the generated `Solution Items` folder in a `.slnx` file synchronized with a small glob list stored in an XML comment.

The tool is intended to make files such as `.editorconfig`, repository documentation, build scripts, and selected GitHub workflow files visible from the solution without hand-editing the `.slnx` file whenever those files change.

## Goals

- Build a .NET CLI tool that follows the RepoConventions repository shape: small executable project, `System.CommandLine` command surface, integration-style tests with temporary repositories, and package-as-tool metadata.
- Read and write the glob declaration from a `.slnx` XML comment in this form: `<!-- dotnet-solution-items: *; .github/workflows/*; !build.sh -->`.
- Treat globs as implicitly rooted at the directory containing the solution file.
- Use the `Glob` NuGet package, version `1.1.9`, for glob expansion.
- Maintain only the `Folder` and `File` XML elements immediately following the `dotnet-solution-items` comment.
- Generate deterministic folder and file ordering so repeated `update` runs are stable.
- Exclude solution files and C# project files automatically.
- Enforce a default limit of 100 generated files and folders, with `--limit <n>` available when callers intentionally need more.
- Match only files, not directories, and create solution folders only as ancestors of matched files.
- Provide `add`, `remove`, `list`, and `update` commands.

## Non-Goals

- Do not support legacy `.sln` files in the first version.
- Do not manage arbitrary solution folders outside the annotated `Solution Items` block.
- Do not infer globs from existing solution items.
- Do not add a watch mode, verify mode, machine-readable output, or automatic Git commits yet.
- Do not implement custom glob matching logic beyond the behavior needed to root and filter paths before passing patterns to `Glob`.

## CLI Surface

The root command should support these global options for every subcommand:

- `--solution <path>`: optional solution file or directory override. If omitted, use the current directory. If the value is a directory, require exactly one `.slnx` file in that directory. If the value is a file, require that it exists and ends with `.slnx`.
- `--limit <n>`: maximum total generated `Folder` and `File` elements. Defaults to `100`. Reject values less than `1`.
- `--force`: supported by `add`; allows the first `add` to replace an existing unmarked `Solution Items` block.

Commands:

- `add <glob...>`: add the globs to the declaration if they are not already present, then update the managed solution items.
- `remove <glob...>`: remove the matching globs from the declaration, then update the managed solution items. If the last glob is removed, remove the marker comment and the managed folder block.
- `list`: print the configured globs and the files they currently expand to.
- `update`: rewrite the managed solution items from the configured globs.

With no arguments, show help. Support the standard `--help` and `--version` behavior from `System.CommandLine`.

## Solution Discovery

Resolution should happen once near the CLI boundary and downstream code should receive normalized paths.

- When `--solution` is omitted, inspect the current process directory for `.slnx` files.
- When `--solution` points to a directory, inspect that directory for `.slnx` files.
- Fail with a clear error if discovery finds zero or more than one `.slnx` file.
- When `--solution` points to a file, use that exact file and fail if it does not exist or does not have the `.slnx` extension.
- Set `SolutionDirectory` to the directory containing the resolved solution file. All glob expansion and relative path display should be based on that directory.

Suggested internal type:

- `ResolvedSolutionPath.SolutionPath`
- `ResolvedSolutionPath.SolutionDirectory`
- `ResolvedSolutionPath.DisplayPath`

## Glob Semantics

Glob handling should be explicit and deterministic.

- Split the marker comment payload on semicolons.
- Split `add` and `remove` arguments on semicolons too, so `add "*.md; *.props"` and `add "*.md" "*.props"` are equivalent.
- Trim whitespace around each glob.
- Ignore empty entries when reading existing comments.
- Preserve glob text when writing the comment, joined with a semicolon followed by a space.
- Treat a leading `!` as an exclusion glob. The stored glob remains prefixed with `!`, but the matcher should evaluate the remainder.
- Evaluate include globs in declaration order and then remove any paths matched by exclusion globs.
- Automatically exclude files ignored by Git, `.slnx` files, and `*.csproj` files even if they match an include glob.
- Evaluate matches against files only. A glob such as `*` matches files in the solution directory but does not add directories in the solution directory.
- Return only files as `File` entries. Directories exist only as generated `Folder` entries needed to contain those files.
- If the `Glob` API returns directories for a pattern, filter them out before generating the managed XML block.
- Normalize generated paths to forward slashes, relative to `SolutionDirectory`.
- Use ordinal, case-insensitive path de-duplication on Windows and ordinal de-duplication on non-Windows platforms. Write generated folders in ordinal ascending order and file entries within each folder in invariant culture order for stable, browsable output.

Glob identity for `add` and `remove` should use exact text after trimming whitespace, not slash or path normalization. For example, ` docs/*.md ` and `docs/*.md` are the same stored glob after trimming, but `docs/*.md` and `docs\*.md` are different, and `README.md` and `./README.md` are different. This keeps the command from rewriting user-entered glob text while still avoiding duplicate entries caused only by surrounding whitespace.

## Managed XML Block

The owned region starts at the `dotnet-solution-items:` comment and includes the immediately following contiguous `Folder` elements that represent `/Solution Items/` and any child folders needed as ancestors of matched files.

For a file such as `.github/workflows/build.yaml`, generate:

```xml
<Folder Name="/Solution Items/.github/" />
<Folder Name="/Solution Items/.github/workflows/">
  <File Path=".github/workflows/build.yaml" />
</Folder>
```

Rules:

- Use `System.Xml.Linq` to parse and update XML rather than editing XML with string slicing.
- Load with whitespace preservation so unrelated parts of the solution remain stable where possible.
- If the marker comment is missing and `add` creates the first glob, insert the marker comment and generated folders before the first `Project` element, or at the end of the root when no projects exist.
- If the marker comment is missing and an unmarked `Solution Items` block already exists, `add` should fail with a warning that non-matching items would be removed. `add --force` should insert the marker before that block and replace it with generated folders.
- If the marker comment exists, replace only the owned folder block after it.
- If no files match the current globs, keep the marker comment and omit the generated folder block.
- If the glob list becomes empty, remove the marker comment and the owned folder block.
- Do not create empty `Folder` elements except as ancestors of folders that contain matched files.
- Preserve unrelated `Project`, `Folder`, and other XML elements outside the owned block.
- Emit generated XML using two-space indentation to match the `.slnx` examples used by RepoConventions.

## Limit Handling

Apply the `--limit` value to the total number of generated `Folder` and `File` elements.

- Count every generated `Folder`, including `/Solution Items/`.
- Count every generated `File`.
- Fail before writing when the count is equal to or greater than the limit.
- The default limit is `100`.
- Error messages should include the limit, the resolved solution path, and guidance to pass `--limit <n>` if the result is intentional.

The limit is intentionally conservative so a broad glob such as `**/*` cannot accidentally add the whole repository to the solution.

## Command Behavior

`add <glob...>`:

- Resolve the solution.
- Read the existing marker comment if present.
- Append the glob if it is not already present using exact text comparison after trimming.
- Accept multiple arguments and semicolon-separated globs.
- Create the marker comment if needed.
- If an unmarked `Solution Items` block already exists, fail unless `--force` is supplied.
- Update the managed XML block.
- Print whether the glob was added or already present, plus the number of generated folders and files.

`remove <glob...>`:

- Resolve the solution.
- Fail if the marker comment is missing.
- Remove existing globs using exact text comparison after trimming.
- Accept multiple arguments and semicolon-separated globs.
- If the glob was not present, return success with an "already absent" message and leave the solution unchanged.
- If the last glob was removed, remove the marker comment and managed block.
- Otherwise update the managed XML block.

`list`:

- Resolve the solution.
- Print configured globs in declaration order.
- Print the matched files after includes, exclusions, automatic exclusions, and de-duplication.
- Do not modify the solution.

`update`:

- Resolve the solution.
- If the marker comment is missing, write a warning and leave the solution unchanged.
- If the marker comment has no usable globs, write a warning and leave the solution unchanged.
- Regenerate the managed XML block.
- Write the solution only if the content changed.

## Internal Design

Suggested implementation units:

- `SolutionItemsCli`: command construction and handlers.
- `ResolvedSolutionPath`: solution option and discovery logic.
- `SolutionItemsConfiguration`: marker comment parsing, glob list updates, and comment formatting.
- `SolutionItemsGlobExpander`: rooted glob evaluation, exclusion handling, automatic exclusions, sorting, and limit counting.
- `SolutionItemsXmlUpdater`: XML owned-block detection, generation, and update.
- `GeneratedSolutionItems`: immutable result containing generated folders, files, counts, and display text.

Keep each top-level C# type in its own file, matching the RepoConventions style.

## Project Shape

Create a repository layout similar to RepoConventions:

- `src/DotNetSolutionItems/DotNetSolutionItems.csproj`
- `tests/DotNetSolutionItems.Tests/DotNetSolutionItems.Tests.csproj`
- `tools/Build/Build.csproj` if the repo needs the same build script approach as RepoConventions
- `Directory.Build.props`
- `Directory.Packages.props`
- `README.md`
- `ReleaseNotes.md`
- `SolutionItems.slnx`

Project metadata:

- Pack as a .NET tool with `PackAsTool=true`.
- Set `AssemblyName` and `ToolCommandName` to `dotnet-solution-items`.
- Reference `System.CommandLine` for parsing.
- Reference `Glob` version `1.1.9` through central package management.
- Use nullable reference types, implicit usings, analyzers, warnings-as-errors, and artifact output in line with RepoConventions.

## Test Plan

Favor integration-style tests that create temporary directories and real `.slnx` files.

- Discovery succeeds when the current directory contains exactly one `.slnx` file.
- Discovery fails when there are zero or multiple `.slnx` files.
- `--solution` accepts an exact `.slnx` file.
- `--solution` accepts a directory containing exactly one `.slnx` file.
- `add` creates the marker comment and managed block in a solution with projects but no solution items.
- First `add` fails when an unmarked `Solution Items` block already exists.
- `add --force` replaces an unmarked `Solution Items` block with the generated block.
- `add` accepts multiple arguments and semicolon-separated globs.
- `add` does not duplicate an existing glob.
- `remove` removes one glob and updates the generated block.
- `remove` accepts multiple arguments and semicolon-separated globs.
- `remove` removes the marker comment and generated block when the last glob is removed.
- `list` reports configured globs and expanded files without writing the solution.
- `update` adds new matching files and removes files that no longer match.
- `update` writes a warning and leaves the solution unchanged when the marker comment is missing.
- Include and exclude globs interact correctly, including `!build.sh`.
- `.slnx` and `*.csproj` files are excluded automatically.
- `*` matches root-level files but does not add root-level directories as empty solution folders.
- Nested folders are generated for nested matching files.
- Directory glob matches do not create solution folders unless files inside those directories are matched.
- Empty matches keep the marker comment but remove generated folders.
- The default limit fails before writing when generated folders plus files reach `100`.
- `--limit <n>` allows a deliberately larger generated block.
- XML outside the owned block is preserved.

## Documentation

- Add README quick start instructions for installing or running with `dnx`.
- Document the marker comment format and the semicolon-separated glob list.
- Document rooted glob behavior, exclusion globs, automatic exclusions, and the default limit.
- Include examples for `add`, `add --force`, `remove`, `list`, `update`, `--solution`, and `--limit`.
- Add a short troubleshooting section for ambiguous solution discovery and limit failures.

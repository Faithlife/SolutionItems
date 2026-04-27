# dotnet-solution-items

Maintains the generated `Solution Items` folder in a `.slnx` file from a semicolon-separated glob list stored in an XML comment.

## Quick Start

Install the tool globally, or run it ad hoc with `dnx`.

```pwsh
dotnet tool install --global dotnet-solution-items
# or
dnx dotnet-solution-items --help
```

Add globs to a solution:

```pwsh
dotnet-solution-items add "*"
dotnet-solution-items add ".github/workflows/*" "!build.sh"
dotnet-solution-items add "*.md; *.props"
```

The tool stores the globs in a marker comment and owns the contiguous `Folder` block immediately below it.

```xml
<!-- dotnet-solution-items: *; .github/workflows/*; !build.sh -->
<Folder Name="/Solution Items/">
  <File Path=".editorconfig" />
  <File Path="README.md" />
</Folder>
```

## Commands

- `add <glob>` adds a glob and updates the generated solution items.
- `remove <glob>` removes a glob and updates the generated solution items.
- `list` prints configured globs and the files they match.
- `update` refreshes the generated solution items. If the marker comment is missing, it writes a warning and leaves the solution unchanged.

`add` and `remove` accept one or more glob arguments. Each argument may also contain semicolon-separated globs, and both forms are treated the same way.

On the first `add`, if the solution already has an unmarked `Solution Items` block, the command fails with a warning because non-matching existing items would be removed. Pass `--force` to replace that block with the generated one.

## Options

- `--solution <path>` points to a specific `.slnx` file or to a directory containing exactly one `.slnx` file.
- `--limit <n>` changes the maximum total generated `Folder` and `File` elements. The default is `100`.
- `--force` allows `add` to replace an existing unmarked `Solution Items` block when creating the marker comment.

Globs are rooted at the solution directory and match files only. Solution files and `*.csproj` files are automatically excluded.

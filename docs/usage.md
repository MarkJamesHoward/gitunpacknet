# Usage

## Basic Usage

```bash
# Unpack in current directory
git-unpack

# Unpack a specific repository
git-unpack /path/to/repo

# Unpack using .git directory directly
git-unpack /path/to/repo/.git
```

## Options

| Option | Description |
|--------|-------------|
| `-d, --delete` | Delete pack files and packed-refs after unpacking |
| `-v, --verbose` | Show each object/ref as it's unpacked |
| `-h, --help` | Show help message |

## Examples

### Unpack and keep pack files

```bash
git-unpack /path/to/repo
```

Output:
```
Git directory: /path/to/repo/.git
Found 1 pack file(s)

Processing: pack-abc123.pack
Pack file: version 2, 150 objects
Unpacking 150 objects...

Unpacked 150 new objects (150 total)

Unpacking refs from packed-refs...
Unpacked 3 new refs (3 total)

Done!
```

### Unpack and delete pack files

```bash
git-unpack -d /path/to/repo
```

This will unpack all objects and refs, then remove the `.pack`, `.idx`, and `packed-refs` files.

### Verbose output

```bash
git-unpack -v /path/to/repo
```

Shows each object SHA and type as it's unpacked:
```
  abc123def456... (commit)
  789012345678... (tree)
  fedcba987654... (blob)
  refs/heads/main -> abc123d
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Error (no .git directory, invalid pack file, etc.) |

# git-unpack

CLI tool to unpack Git pack files to loose objects.

[![NuGet](https://img.shields.io/nuget/v/git-unpack.svg)](https://www.nuget.org/packages/git-unpack)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Installation

```bash
dotnet tool install -g git-unpack
```

## Usage

```bash
# Unpack in current repository
git-unpack

# Unpack a specific repository
git-unpack /path/to/repo

# Unpack and delete pack files
git-unpack -d

# Verbose output
git-unpack -v
```

## Options

| Option | Description |
|--------|-------------|
| `-d, --delete` | Delete pack files and packed-refs after unpacking |
| `-v, --verbose` | Show each object/ref as it's unpacked |
| `-h, --help` | Show help message |

## What does it do?

Git stores objects in two formats:
- **Loose objects**: Individual files in `.git/objects/`
- **Pack files**: Compressed bundles in `.git/objects/pack/`

Git also stores refs in two formats:
- **Loose refs**: Individual files in `.git/refs/`
- **Packed refs**: A single `.git/packed-refs` file

This tool converts pack files back to loose objects and packed refs back to individual ref files. Useful for debugging, recovery, or understanding Git internals.

## Requirements

- [.NET 9.0](https://dotnet.microsoft.com/download) or later

## License

MIT

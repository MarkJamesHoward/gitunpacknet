# git-unpack

> CLI tool to unpack Git pack files to loose objects

## What is this?

Git stores objects (commits, trees, blobs, tags) in two formats:
- **Loose objects**: Individual compressed files in `.git/objects/XX/YYYY...`
- **Pack files**: Compressed archives in `.git/objects/pack/` that bundle many objects together

Git also stores refs in two formats:
- **Loose refs**: Individual files in `.git/refs/`
- **Packed refs**: A single `.git/packed-refs` file

`git-unpack` converts pack files back to loose objects and packed refs back to individual ref files, which can be useful for:
- Debugging Git repositories
- Recovering objects from pack files
- Understanding Git internals
- Educational purposes

## Quick Start

```bash
# Install as a global .NET tool
dotnet tool install -g git-unpack

# Run in a Git repository
cd your-repo
git-unpack

# Or specify a path
git-unpack /path/to/repo
```

## Features

- Parses Git pack files (version 2 and 3)
- Decompresses zlib-compressed objects
- Reconstructs delta objects (OFS_DELTA and REF_DELTA)
- Unpacks packed refs to individual ref files
- Supports thin packs
- Zero dependencies (uses .NET built-in libraries only)

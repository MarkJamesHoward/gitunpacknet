# API

You can also use git-unpack programmatically by referencing the project.

## Unpacker Class

```csharp
using GitUnpack;

var unpacker = new Unpacker("/path/to/.git");
var result = unpacker.Unpack(deletePackFiles: false, verbose: false);

Console.WriteLine($"Total: {result.TotalObjects}, New: {result.NewObjects}");
// Total: 150, New: 150
```

### Constructor

```csharp
new Unpacker(string gitDir)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `gitDir` | `string` | Path to the `.git` directory |

### Methods

#### `Unpack(bool deletePackFiles, bool verbose)`

Unpacks all pack files and packed refs in the repository.

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `deletePackFiles` | `bool` | `false` | Delete pack files and packed-refs after unpacking |
| `verbose` | `bool` | `false` | Log each object/ref as it's unpacked |

**Returns:** `UnpackResult`

```csharp
record UnpackResult(
    int TotalObjects,  // Total objects processed
    int NewObjects,    // New objects written (excludes existing)
    int PackFiles,     // Number of pack files processed
    int TotalRefs,     // Total refs processed
    int NewRefs        // New refs written (excludes existing)
);
```

## PackReader Class

Lower-level API for reading pack files directly.

```csharp
using GitUnpack;

var reader = new PackReader("/path/to/.git/objects/pack/pack-xxx.pack");
var objects = reader.Parse();

foreach (var (sha, obj) in objects)
{
    Console.WriteLine($"{sha}: {obj.Type} ({obj.Data.Length} bytes)");
}
```

### Constructor

```csharp
new PackReader(string packPath, string? objectsDir = null)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `packPath` | `string` | Path to the `.pack` file |
| `objectsDir` | `string?` | Optional: path to objects dir for thin pack support |

### Methods

#### `Parse()`

Parses the pack file and returns a dictionary of all objects.

**Returns:** `Dictionary<string, GitObject>`

```csharp
record GitObject(string Type, byte[] Data);
```

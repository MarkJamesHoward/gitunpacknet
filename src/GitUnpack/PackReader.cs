using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GitUnpack;

class PackReader
{
    private const int OBJ_COMMIT = 1;
    private const int OBJ_TREE = 2;
    private const int OBJ_BLOB = 3;
    private const int OBJ_TAG = 4;
    private const int OBJ_OFS_DELTA = 6;
    private const int OBJ_REF_DELTA = 7;

    private static readonly Dictionary<int, string> TypeNames = new()
    {
        [OBJ_COMMIT] = "commit",
        [OBJ_TREE] = "tree",
        [OBJ_BLOB] = "blob",
        [OBJ_TAG] = "tag"
    };

    private readonly string _packPath;
    private readonly string? _objectsDir;
    private readonly byte[] _buffer;
    private int _offset;
    private readonly Dictionary<string, GitObject> _objects = new();
    private readonly Dictionary<int, string> _offsetToSha = new();
    private List<PendingDelta> _pendingDeltas = new();

    public PackReader(string packPath, string? objectsDir = null)
    {
        _packPath = packPath;
        _objectsDir = objectsDir;
        _buffer = File.ReadAllBytes(packPath);
        _offset = 0;
    }

    private GitObject? ReadLooseObject(string sha)
    {
        if (_objectsDir == null) return null;

        var objectPath = Path.Combine(_objectsDir, sha[..2], sha[2..]);
        if (!File.Exists(objectPath)) return null;

        try
        {
            var compressed = File.ReadAllBytes(objectPath);
            var data = ZlibDecompress(compressed);

            // Parse the header: "type size\0"
            var nullIndex = Array.IndexOf(data, (byte)0);
            var header = Encoding.ASCII.GetString(data, 0, nullIndex);
            var spaceIndex = header.IndexOf(' ');
            var type = header[..spaceIndex];
            var content = new byte[data.Length - nullIndex - 1];
            Buffer.BlockCopy(data, nullIndex + 1, content, 0, content.Length);

            return new GitObject(type, content);
        }
        catch
        {
            return null;
        }
    }

    private byte[] Read(int length)
    {
        var data = new byte[length];
        Buffer.BlockCopy(_buffer, _offset, data, 0, length);
        _offset += length;
        return data;
    }

    private uint ReadUInt32BE()
    {
        var val = (uint)((_buffer[_offset] << 24) | (_buffer[_offset + 1] << 16) |
                         (_buffer[_offset + 2] << 8) | _buffer[_offset + 3]);
        _offset += 4;
        return val;
    }

    public Dictionary<string, GitObject> Parse()
    {
        // Parse header
        var signature = Encoding.ASCII.GetString(Read(4));
        if (signature != "PACK")
            throw new Exception($"Invalid pack signature: {signature}");

        var version = ReadUInt32BE();
        if (version != 2 && version != 3)
            throw new Exception($"Unsupported pack version: {version}");

        var numObjects = ReadUInt32BE();
        Console.WriteLine($"Pack file: version {version}, {numObjects} objects");

        // First pass: read all objects
        for (uint i = 0; i < numObjects; i++)
        {
            ReadObject();
        }

        // Second pass: resolve deltas
        ResolveDeltas();

        return _objects;
    }

    private void ReadObject()
    {
        var objectOffset = _offset;

        // Read variable-length type and size
        var b = _buffer[_offset++];
        var type = (b >> 4) & 0x07;
        long size = b & 0x0f;
        var shift = 4;

        while ((b & 0x80) != 0)
        {
            b = _buffer[_offset++];
            size |= (long)(b & 0x7f) << shift;
            shift += 7;
        }

        if (type == OBJ_OFS_DELTA)
        {
            var baseOffset = ReadOffsetDelta(objectOffset);
            var data = DecompressData();
            _pendingDeltas.Add(new PendingDelta(objectOffset, type, data, BaseOffset: baseOffset));
            return;
        }

        if (type == OBJ_REF_DELTA)
        {
            var baseShaBytes = Read(20);
            var baseSha = Convert.ToHexString(baseShaBytes).ToLowerInvariant();
            var data = DecompressData();
            _pendingDeltas.Add(new PendingDelta(objectOffset, type, data, BaseSha: baseSha));
            return;
        }

        // Regular object
        var objData = DecompressData();

        if (!TypeNames.TryGetValue(type, out var typeName))
            throw new Exception($"Unknown object type: {type}");

        var sha = ComputeSha(typeName, objData);
        _objects[sha] = new GitObject(typeName, objData);
        _offsetToSha[objectOffset] = sha;
    }

    private int ReadOffsetDelta(int objectOffset)
    {
        var b = _buffer[_offset++];
        var offset = b & 0x7f;

        while ((b & 0x80) != 0)
        {
            b = _buffer[_offset++];
            offset = ((offset + 1) << 7) | (b & 0x7f);
        }

        return objectOffset - offset;
    }

    private byte[] DecompressData()
    {
        var remaining = new byte[_buffer.Length - _offset];
        Buffer.BlockCopy(_buffer, _offset, remaining, 0, remaining.Length);

        var result = InflateWithConsumedBytes(remaining);
        _offset += result.BytesConsumed;
        return result.Data;
    }

    private (byte[] Data, int BytesConsumed) InflateWithConsumedBytes(byte[] input)
    {
        // Try decompressing with all remaining data first
        try
        {
            var data = ZlibDecompress(input);
            // Binary search for minimum required bytes
            var bytesConsumed = FindMinimumInflateSize(input, data.Length);
            return (data, bytesConsumed);
        }
        catch
        {
            // Try incremental approach
            return IncrementalInflate(input);
        }
    }

    private int FindMinimumInflateSize(byte[] input, int expectedOutputSize)
    {
        var low = 2; // Minimum zlib stream is at least 2 bytes (header)
        var high = input.Length;
        var lastGood = high;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            try
            {
                var result = ZlibDecompress(input.AsSpan(0, mid).ToArray());
                if (result.Length == expectedOutputSize)
                {
                    lastGood = mid;
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }
            catch
            {
                low = mid + 1;
            }
        }

        return lastGood;
    }

    private (byte[] Data, int BytesConsumed) IncrementalInflate(byte[] input)
    {
        byte[]? lastValidData = null;
        var lastValidSize = 0;

        for (var size = 10; size <= input.Length; size += Math.Max(1, size / 10))
        {
            try
            {
                var chunk = input.AsSpan(0, size).ToArray();
                var data = ZlibDecompress(chunk);
                lastValidData = data;
                lastValidSize = size;

                var minSize = FindMinimumInflateSize(input, data.Length);
                return (data, minSize);
            }
            catch
            {
                continue;
            }
        }

        // Last resort: try with all data
        try
        {
            var data = ZlibDecompress(input);
            return (data, input.Length);
        }
        catch (Exception e)
        {
            if (lastValidData != null)
                return (lastValidData, lastValidSize);
            throw new Exception($"Failed to decompress: {e.Message}");
        }
    }

    private static byte[] ZlibDecompress(byte[] input)
    {
        // Try ZLibStream first (handles zlib header automatically)
        try
        {
            using var inputStream = new MemoryStream(input);
            using var zlib = new ZLibStream(inputStream, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            // Fall back to raw DeflateStream (no header)
            using var inputStream = new MemoryStream(input);
            using var deflate = new DeflateStream(inputStream, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }
    }

    private void ResolveDeltas()
    {
        var resolved = true;
        var iterations = 0;
        var maxIterations = _pendingDeltas.Count + 1;

        while (resolved && _pendingDeltas.Count > 0 && iterations < maxIterations)
        {
            resolved = false;
            iterations++;

            var stillPending = new List<PendingDelta>();

            foreach (var delta in _pendingDeltas)
            {
                string? baseSha = null;
                GitObject? baseObj = null;

                if (delta.BaseOffset.HasValue)
                {
                    if (_offsetToSha.TryGetValue(delta.BaseOffset.Value, out baseSha))
                    {
                        _objects.TryGetValue(baseSha, out baseObj);
                    }
                }
                else if (delta.BaseSha != null)
                {
                    baseSha = delta.BaseSha;
                    _objects.TryGetValue(baseSha, out baseObj);

                    // Try to read from loose objects (thin pack support)
                    if (baseObj == null)
                    {
                        baseObj = ReadLooseObject(baseSha);
                        if (baseObj != null)
                        {
                            _objects[baseSha] = baseObj;
                        }
                    }
                }

                if (baseObj == null)
                {
                    stillPending.Add(delta);
                    continue;
                }

                var resolvedData = ApplyDelta(baseObj.Data, delta.DeltaData);
                var sha = ComputeSha(baseObj.Type, resolvedData);

                _objects[sha] = new GitObject(baseObj.Type, resolvedData);
                _offsetToSha[delta.ObjectOffset] = sha;
                resolved = true;
            }

            _pendingDeltas = stillPending;
        }

        if (_pendingDeltas.Count > 0)
        {
            Console.Error.WriteLine($"Warning: {_pendingDeltas.Count} delta objects could not be resolved");
        }
    }

    private static byte[] ApplyDelta(byte[] baseData, byte[] deltaData)
    {
        var offset = 0;

        // Read base size (variable length)
        long baseSize = 0;
        var shift = 0;
        byte b;
        do
        {
            b = deltaData[offset++];
            baseSize |= (long)(b & 0x7f) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        // Read result size (variable length)
        long resultSize = 0;
        shift = 0;
        do
        {
            b = deltaData[offset++];
            resultSize |= (long)(b & 0x7f) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        var result = new byte[resultSize];
        var resultOffset = 0;

        while (offset < deltaData.Length)
        {
            var cmd = deltaData[offset++];

            if ((cmd & 0x80) != 0)
            {
                // Copy from base
                var copyOffset = 0;
                var copySize = 0;

                if ((cmd & 0x01) != 0) copyOffset |= deltaData[offset++];
                if ((cmd & 0x02) != 0) copyOffset |= deltaData[offset++] << 8;
                if ((cmd & 0x04) != 0) copyOffset |= deltaData[offset++] << 16;
                if ((cmd & 0x08) != 0) copyOffset |= deltaData[offset++] << 24;

                if ((cmd & 0x10) != 0) copySize |= deltaData[offset++];
                if ((cmd & 0x20) != 0) copySize |= deltaData[offset++] << 8;
                if ((cmd & 0x40) != 0) copySize |= deltaData[offset++] << 16;

                if (copySize == 0) copySize = 0x10000;

                Buffer.BlockCopy(baseData, copyOffset, result, resultOffset, copySize);
                resultOffset += copySize;
            }
            else if (cmd != 0)
            {
                // Insert new data
                Buffer.BlockCopy(deltaData, offset, result, resultOffset, cmd);
                offset += cmd;
                resultOffset += cmd;
            }
            else
            {
                throw new Exception("Invalid delta command: 0");
            }
        }

        return result;
    }

    private static string ComputeSha(string type, byte[] data)
    {
        var header = Encoding.ASCII.GetBytes($"{type} {data.Length}\0");
        var full = new byte[header.Length + data.Length];
        Buffer.BlockCopy(header, 0, full, 0, header.Length);
        Buffer.BlockCopy(data, 0, full, header.Length, data.Length);
        var hash = SHA1.HashData(full);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

record GitObject(string Type, byte[] Data);
record PendingDelta(int ObjectOffset, int Type, byte[] DeltaData, int? BaseOffset = null, string? BaseSha = null);

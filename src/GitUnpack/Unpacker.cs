using System.IO.Compression;

namespace GitUnpack;

class Unpacker
{
    private readonly string _gitDir;
    private readonly string _objectsDir;
    private readonly string _packDir;

    public Unpacker(string gitDir)
    {
        _gitDir = gitDir;
        _objectsDir = Path.Combine(gitDir, "objects");
        _packDir = Path.Combine(_objectsDir, "pack");
    }

    public string[] FindPackFiles()
    {
        if (!Directory.Exists(_packDir))
            return Array.Empty<string>();

        return Directory.GetFiles(_packDir, "*.pack");
    }

    public bool HasPackedRefs()
    {
        return File.Exists(Path.Combine(_gitDir, "packed-refs"));
    }

    public bool WriteObject(string sha, string type, byte[] data)
    {
        var dir = Path.Combine(_objectsDir, sha[..2]);
        var file = Path.Combine(dir, sha[2..]);

        // Skip if object already exists
        if (File.Exists(file))
            return false;

        // Create directory if needed
        Directory.CreateDirectory(dir);

        // Create Git object format: type + space + size + null + data
        var header = System.Text.Encoding.ASCII.GetBytes($"{type} {data.Length}\0");
        var content = new byte[header.Length + data.Length];
        Buffer.BlockCopy(header, 0, content, 0, header.Length);
        Buffer.BlockCopy(data, 0, content, header.Length, data.Length);

        // Compress with zlib (deflate with zlib header)
        var compressed = ZlibCompress(content);
        File.WriteAllBytes(file, compressed);
        return true;
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        // Write zlib header (CMF=0x78, FLG=0x01 for default compression)
        output.WriteByte(0x78);
        output.WriteByte(0x01);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        // Write Adler-32 checksum
        var checksum = Adler32(data);
        output.WriteByte((byte)(checksum >> 24));
        output.WriteByte((byte)(checksum >> 16));
        output.WriteByte((byte)(checksum >> 8));
        output.WriteByte((byte)checksum);

        return output.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    public UnpackResult Unpack(bool deletePackFiles = false, bool verbose = false)
    {
        var packFiles = FindPackFiles();
        int totalObjects = 0;
        int newObjects = 0;

        if (packFiles.Length == 0 && !HasPackedRefs())
        {
            Console.WriteLine("No pack files or packed-refs found. Nothing to unpack.");
            return new UnpackResult(0, 0, 0, 0, 0);
        }

        if (packFiles.Length > 0)
        {
            Console.WriteLine($"Found {packFiles.Length} pack file(s)");
        }

        foreach (var packFile in packFiles)
        {
            Console.WriteLine($"\nProcessing: {Path.GetFileName(packFile)}");

            var reader = new PackReader(packFile, _objectsDir);
            var objects = reader.Parse();

            Console.WriteLine($"Unpacking {objects.Count} objects...");

            foreach (var (sha, obj) in objects)
            {
                totalObjects++;
                var written = WriteObject(sha, obj.Type, obj.Data);
                if (written)
                {
                    newObjects++;
                    if (verbose)
                    {
                        Console.WriteLine($"  {sha} ({obj.Type})");
                    }
                }
            }
        }

        Console.WriteLine($"\nUnpacked {newObjects} new objects ({totalObjects} total)");

        if (deletePackFiles)
        {
            Console.WriteLine("\nDeleting pack files...");
            foreach (var packFile in packFiles)
            {
                var idxFile = Path.ChangeExtension(packFile, ".idx");

                File.Delete(packFile);
                Console.WriteLine($"  Deleted: {Path.GetFileName(packFile)}");

                if (File.Exists(idxFile))
                {
                    File.Delete(idxFile);
                    Console.WriteLine($"  Deleted: {Path.GetFileName(idxFile)}");
                }
            }
        }

        // Unpack refs from packed-refs file
        var refsResult = UnpackRefs(deletePackFiles, verbose);

        return new UnpackResult(totalObjects, newObjects, packFiles.Length, refsResult.TotalRefs, refsResult.NewRefs);
    }

    private RefsResult UnpackRefs(bool deletePackedRefs, bool verbose)
    {
        var refsUnpacker = new RefsUnpacker(_gitDir);

        if (!refsUnpacker.HasPackedRefs())
            return new RefsResult(0, 0);

        Console.WriteLine("\nUnpacking refs from packed-refs...");
        var result = refsUnpacker.Unpack(deletePackedRefs, verbose);
        Console.WriteLine($"Unpacked {result.NewRefs} new refs ({result.TotalRefs} total)");

        return result;
    }
}

record UnpackResult(int TotalObjects, int NewObjects, int PackFiles, int TotalRefs, int NewRefs);

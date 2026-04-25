using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GitUnpack;

namespace GitUnpack.Tests;

[TestFixture]
public class UnpackerTests
{
    private string _tempDir = null!;
    private string _gitDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gitunpack-test-{Guid.NewGuid():N}");
        _gitDir = Path.Combine(_tempDir, ".git");
        Directory.CreateDirectory(Path.Combine(_gitDir, "objects", "pack"));
        Directory.CreateDirectory(Path.Combine(_gitDir, "refs"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void FindPackFiles_ReturnsEmpty_WhenNoPackFiles()
    {
        var unpacker = new Unpacker(_gitDir);
        var result = unpacker.FindPackFiles();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void FindPackFiles_ReturnsEmpty_WhenPackDirMissing()
    {
        Directory.Delete(Path.Combine(_gitDir, "objects", "pack"));
        var unpacker = new Unpacker(_gitDir);
        var result = unpacker.FindPackFiles();
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void HasPackedRefs_ReturnsFalse_WhenNoFile()
    {
        var unpacker = new Unpacker(_gitDir);
        Assert.That(unpacker.HasPackedRefs(), Is.False);
    }

    [Test]
    public void HasPackedRefs_ReturnsTrue_WhenFileExists()
    {
        File.WriteAllText(Path.Combine(_gitDir, "packed-refs"), "# pack-refs\n");
        var unpacker = new Unpacker(_gitDir);
        Assert.That(unpacker.HasPackedRefs(), Is.True);
    }

    [Test]
    public void WriteObject_CreatesLooseObject()
    {
        var unpacker = new Unpacker(_gitDir);
        var data = Encoding.UTF8.GetBytes("hello world");
        var sha = ComputeSha("blob", data);

        var written = unpacker.WriteObject(sha, "blob", data);

        Assert.That(written, Is.True);

        var objectPath = Path.Combine(_gitDir, "objects", sha[..2], sha[2..]);
        Assert.That(File.Exists(objectPath), Is.True);

        // Verify we can decompress it and it has the right content
        var compressed = File.ReadAllBytes(objectPath);
        var decompressed = ZlibDecompress(compressed);
        var expectedHeader = $"blob {data.Length}\0";
        var expected = Encoding.ASCII.GetBytes(expectedHeader).Concat(data).ToArray();
        Assert.That(decompressed, Is.EqualTo(expected));
    }

    [Test]
    public void WriteObject_SkipsDuplicate()
    {
        var unpacker = new Unpacker(_gitDir);
        var data = Encoding.UTF8.GetBytes("hello world");
        var sha = ComputeSha("blob", data);

        unpacker.WriteObject(sha, "blob", data);
        var writtenAgain = unpacker.WriteObject(sha, "blob", data);

        Assert.That(writtenAgain, Is.False);
    }

    [Test]
    public void Unpack_ReturnsZeros_WhenNothingToUnpack()
    {
        Directory.Delete(Path.Combine(_gitDir, "objects", "pack"));
        var unpacker = new Unpacker(_gitDir);

        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var result = unpacker.Unpack();
            Assert.That(result.TotalObjects, Is.EqualTo(0));
            Assert.That(result.NewObjects, Is.EqualTo(0));
            Assert.That(result.PackFiles, Is.EqualTo(0));
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    private static string ComputeSha(string type, byte[] data)
    {
        var header = Encoding.ASCII.GetBytes($"{type} {data.Length}\0");
        var full = new byte[header.Length + data.Length];
        Buffer.BlockCopy(header, 0, full, 0, header.Length);
        Buffer.BlockCopy(data, 0, full, header.Length, data.Length);
        return Convert.ToHexString(SHA1.HashData(full)).ToLowerInvariant();
    }

    private static byte[] ZlibDecompress(byte[] input)
    {
        using var inputStream = new MemoryStream(input, 2, input.Length - 2);
        using var deflate = new DeflateStream(inputStream, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}

[TestFixture]
public class RefsUnpackerTests
{
    private string _tempDir = null!;
    private string _gitDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gitunpack-test-{Guid.NewGuid():N}");
        _gitDir = Path.Combine(_tempDir, ".git");
        Directory.CreateDirectory(Path.Combine(_gitDir, "refs"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void HasPackedRefs_ReturnsFalse_WhenMissing()
    {
        var unpacker = new RefsUnpacker(_gitDir);
        Assert.That(unpacker.HasPackedRefs(), Is.False);
    }

    [Test]
    public void Unpack_CreatesRefFiles()
    {
        var sha = "a" + new string('0', 39);
        File.WriteAllText(Path.Combine(_gitDir, "packed-refs"),
            $"# pack-refs with: peeled fully-peeled sorted\n{sha} refs/heads/main\n");

        var unpacker = new RefsUnpacker(_gitDir);

        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var result = unpacker.Unpack();
            Assert.That(result.TotalRefs, Is.EqualTo(1));
            Assert.That(result.NewRefs, Is.EqualTo(1));

            var refPath = Path.Combine(_gitDir, "refs", "heads", "main");
            Assert.That(File.Exists(refPath), Is.True);
            Assert.That(File.ReadAllText(refPath).Trim(), Is.EqualTo(sha));
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Test]
    public void Unpack_SkipsExistingRefs()
    {
        var sha = "b" + new string('0', 39);
        File.WriteAllText(Path.Combine(_gitDir, "packed-refs"),
            $"{sha} refs/heads/main\n");

        Directory.CreateDirectory(Path.Combine(_gitDir, "refs", "heads"));
        File.WriteAllText(Path.Combine(_gitDir, "refs", "heads", "main"), "existing\n");

        var unpacker = new RefsUnpacker(_gitDir);

        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var result = unpacker.Unpack();
            Assert.That(result.TotalRefs, Is.EqualTo(1));
            Assert.That(result.NewRefs, Is.EqualTo(0));
            Assert.That(File.ReadAllText(Path.Combine(_gitDir, "refs", "heads", "main")).Trim(),
                Is.EqualTo("existing"));
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Test]
    public void Unpack_SkipsPeeledAndComments()
    {
        var sha = "c" + new string('0', 39);
        var content = $"# comment\n{sha} refs/tags/v1.0\n^{sha}\n\n";
        File.WriteAllText(Path.Combine(_gitDir, "packed-refs"), content);

        var unpacker = new RefsUnpacker(_gitDir);

        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var result = unpacker.Unpack();
            Assert.That(result.TotalRefs, Is.EqualTo(1));
            Assert.That(result.NewRefs, Is.EqualTo(1));
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Test]
    public void Unpack_DeletesPackedRefs_WhenRequested()
    {
        var sha = "d" + new string('0', 39);
        File.WriteAllText(Path.Combine(_gitDir, "packed-refs"),
            $"{sha} refs/heads/main\n");

        var unpacker = new RefsUnpacker(_gitDir);

        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            unpacker.Unpack(deletePackedRefs: true);
            Assert.That(File.Exists(Path.Combine(_gitDir, "packed-refs")), Is.False);
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }
}

[TestFixture]
public class PackReaderTests
{
    [Test]
    public void Parse_ThrowsOnInvalidSignature()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, Encoding.ASCII.GetBytes("NOTPACK"));
            var reader = new PackReader(tempFile);
            Assert.Throws<Exception>(() => reader.Parse());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void Parse_ThrowsOnUnsupportedVersion()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var data = new byte[12];
            Encoding.ASCII.GetBytes("PACK").CopyTo(data, 0);
            data[4] = 0; data[5] = 0; data[6] = 0; data[7] = 99;
            data[8] = 0; data[9] = 0; data[10] = 0; data[11] = 0;
            File.WriteAllBytes(tempFile, data);

            var reader = new PackReader(tempFile);
            Assert.Throws<Exception>(() => reader.Parse());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void Parse_HandlesEmptyPack()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var data = new byte[12];
            Encoding.ASCII.GetBytes("PACK").CopyTo(data, 0);
            data[4] = 0; data[5] = 0; data[6] = 0; data[7] = 2;
            data[8] = 0; data[9] = 0; data[10] = 0; data[11] = 0;
            File.WriteAllBytes(tempFile, data);

            var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                var reader = new PackReader(tempFile);
                var objects = reader.Parse();
                Assert.That(objects, Is.Empty);
            }
            finally
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

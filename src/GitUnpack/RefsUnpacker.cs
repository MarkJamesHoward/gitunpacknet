using System.Text.RegularExpressions;

namespace GitUnpack;

class RefsUnpacker
{
    private readonly string _gitDir;
    private readonly string _packedRefsPath;

    public RefsUnpacker(string gitDir)
    {
        _gitDir = gitDir;
        _packedRefsPath = Path.Combine(gitDir, "packed-refs");
    }

    public bool HasPackedRefs()
    {
        return File.Exists(_packedRefsPath);
    }

    public RefsResult Unpack(bool deletePackedRefs = false, bool verbose = false)
    {
        if (!HasPackedRefs())
            return new RefsResult(0, 0);

        var content = File.ReadAllText(_packedRefsPath);
        var lines = content.Split('\n');

        var totalRefs = 0;
        var newRefs = 0;

        foreach (var line in lines)
        {
            // Skip empty lines and comments
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            // Handle peeled tags (^sha lines)
            if (line.StartsWith('^'))
                continue;

            // Parse: <sha> <refname>
            var match = Regex.Match(line, @"^([0-9a-f]{40})\s+(.+)$");
            if (!match.Success)
                continue;

            var sha = match.Groups[1].Value;
            var refName = match.Groups[2].Value;
            totalRefs++;

            var written = WriteRef(refName, sha);
            if (written)
            {
                newRefs++;
                if (verbose)
                {
                    Console.WriteLine($"  {refName} -> {sha[..7]}");
                }
            }
        }

        if (deletePackedRefs && newRefs > 0)
        {
            File.Delete(_packedRefsPath);
            Console.WriteLine("  Deleted: packed-refs");
        }

        return new RefsResult(totalRefs, newRefs);
    }

    private bool WriteRef(string refName, string sha)
    {
        var refPath = Path.Combine(_gitDir, refName);

        // Skip if ref already exists as a file
        if (File.Exists(refPath))
            return false;

        // Create directory structure
        var refDir = Path.GetDirectoryName(refPath);
        if (refDir != null)
            Directory.CreateDirectory(refDir);

        // Write the ref file
        File.WriteAllText(refPath, sha + "\n");
        return true;
    }
}

record RefsResult(int TotalRefs, int NewRefs);

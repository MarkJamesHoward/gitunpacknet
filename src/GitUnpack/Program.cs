namespace GitUnpack;

class Program
{
    static void Main(string[] args)
    {
        var options = ParseArgs(args);

        if (options.Help)
        {
            PrintHelp();
            Environment.Exit(0);
        }

        // Resolve the git directory
        string? gitDir;
        var inputPath = Path.GetFullPath(options.Path);

        if (inputPath.EndsWith(".git") && Directory.Exists(inputPath))
        {
            gitDir = inputPath;
        }
        else if (Directory.Exists(Path.Combine(inputPath, ".git")))
        {
            gitDir = Path.Combine(inputPath, ".git");
        }
        else
        {
            gitDir = FindGitDir(inputPath);
        }

        if (gitDir == null)
        {
            Console.Error.WriteLine("Error: Could not find .git directory");
            Console.Error.WriteLine("Run this command from within a Git repository or specify a path");
            Environment.Exit(1);
        }

        Console.WriteLine($"Git directory: {gitDir}");

        // Check if there's anything to unpack
        var packDir = Path.Combine(gitDir, "objects", "pack");
        var packedRefsPath = Path.Combine(gitDir, "packed-refs");

        var hasPackDir = Directory.Exists(packDir);
        var packFiles = hasPackDir
            ? Directory.GetFiles(packDir, "*.pack")
            : Array.Empty<string>();
        var hasPackedRefs = File.Exists(packedRefsPath);

        if (packFiles.Length == 0 && !hasPackedRefs)
        {
            Console.WriteLine("No pack files or packed-refs found. Nothing to unpack.");
            Environment.Exit(0);
        }

        try
        {
            var unpacker = new Unpacker(gitDir);
            unpacker.Unpack(options.DeletePackFiles, options.Verbose);

            Console.WriteLine("\nDone!");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nError: {ex.Message}");
            if (options.Verbose)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            Environment.Exit(1);
        }
    }

    static string? FindGitDir(string startPath)
    {
        var current = Path.GetFullPath(startPath);

        while (true)
        {
            var parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current)
                break;

            var gitDir = Path.Combine(current, ".git");
            if (Directory.Exists(gitDir))
                return gitDir;

            current = parent;
        }

        return null;
    }

    static void PrintHelp()
    {
        Console.WriteLine(@"
git-unpack - Unpack Git pack files and refs to loose format

USAGE:
  git-unpack [options] [path]

ARGUMENTS:
  path              Path to .git directory or repository root
                    (defaults to current directory)

OPTIONS:
  -d, --delete      Delete pack files and packed-refs after unpacking
  -v, --verbose     Show each object/ref as it's unpacked
  -h, --help        Show this help message

UNPACKS:
  - Pack files (.git/objects/pack/*.pack) -> loose objects
  - Packed refs (.git/packed-refs) -> individual ref files

EXAMPLES:
  git-unpack                    Unpack in current repository
  git-unpack /path/to/repo      Unpack in specified repository
  git-unpack .git               Unpack using .git directory directly
  git-unpack -d                 Unpack and delete packed files
");
    }

    static Options ParseArgs(string[] args)
    {
        var options = new Options();

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.Help = true;
                    break;
                case "-d":
                case "--delete":
                    options.DeletePackFiles = true;
                    break;
                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Unknown option: {arg}");
                        Environment.Exit(1);
                    }
                    options.Path = arg;
                    break;
            }
        }

        return options;
    }

    class Options
    {
        public bool DeletePackFiles { get; set; }
        public bool Verbose { get; set; }
        public bool Help { get; set; }
        public string Path { get; set; } = ".";
    }
}

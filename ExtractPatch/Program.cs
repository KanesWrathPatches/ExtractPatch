using ExtractPatch;

if (args.Length <= 0)
{
    Console.WriteLine("No folder provided for maps.");
    return -1;
}
string path = args[0];
if (!Directory.Exists(path))
{
    Console.WriteLine($"Directory {path} not found.");
    return -2;
}

FilterManifest filter;
try
{
    filter = new FilterManifest(path);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
#if DEBUG
    throw;
#else
    return -3;
#endif
}

filter.CommitManifest(path, "patch");

return 0;
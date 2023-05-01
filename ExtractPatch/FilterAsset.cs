using ExtractPatch.Hashing;
using ExtractPatch.Sage;

namespace ExtractPatch;

internal sealed class FilterAsset
{
    public Asset Asset { get; }
    public Chunk Chunk { get; }
    public byte[]? CData { get; }
    public uint InstanceHash { get; }
    public uint RelocationHash { get; }
    public uint ImportsHash { get; }

    public FilterAsset(Asset asset)
    {
        Asset = asset;
        Chunk = asset.GetChunk();
        CData = asset.GetCData();
        InstanceHash = FastHash.GetHashCode(Chunk.InstanceBuffer);
        RelocationHash = FastHash.GetHashCode(Chunk.RelocationBuffer);
        ImportsHash = FastHash.GetHashCode(Chunk.ImportsBuffer);
    }

    public void Commit(string manifestBasePath)
    {
        string assetPath = Path.Combine(manifestBasePath, Asset.FileBasePath.Remove(0, 4)) + ".asset";
        string cdataPath = Path.Combine(manifestBasePath, Asset.CDataPath.Remove(0, 4));

        string assetOutputDirectory = Path.GetDirectoryName(assetPath)!;

        if (!Directory.Exists(assetOutputDirectory))
        {
            Directory.CreateDirectory(assetOutputDirectory);
        }
        using (Stream stream = File.Open(assetPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            new AssetHeader()
            {
                TypeId = Asset.TypeId,
                InstanceId = Asset.InstanceId,
                TypeHash = Asset.TypeHash,
                InstanceHash = Asset.InstanceHash,
                InstanceDataSize = Asset.InstanceDataSize,
                RelocationDataSize = Asset.RelocationDataSize,
                ImportsDataSize = Asset.ImportsDataSize
            }.SaveToStream(stream, false);
            stream.Write(Chunk.InstanceBuffer);
            stream.Write(Chunk.RelocationBuffer);
            stream.Write(Chunk.ImportsBuffer);
            stream.Flush();
        }

        if (CData is null)
        {
            return;
        }

        string cdataOutputDirectory = Path.GetDirectoryName(cdataPath)!;
        if (!Directory.Exists(cdataOutputDirectory))
        {
            Directory.CreateDirectory(cdataOutputDirectory);
        }
        File.WriteAllBytes(cdataPath, CData);
    }

    public override string ToString()
    {
        return Asset.ToString();
    }
}

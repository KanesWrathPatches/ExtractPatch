using ExtractPatch.Hashing;
using ExtractPatch.Sage;

namespace ExtractPatch;

internal sealed class FilterManifest
{
    private List<FilterAsset> _assets = new();

    public FilterManifest(string source)
    {
        foreach (string map in Directory.EnumerateDirectories(source))
        {
            string manifestFile = Path.Combine(map, "map.manifest");
            if (!File.Exists(manifestFile))
            {
                continue;
            }
            Console.WriteLine($"Loading '{manifestFile}'.");
            Manifest manifest = new(manifestFile);
            uint gameScriptListHash = FastHash.GetHashCode("GameScriptList");
            uint terrainTextureAtlasHash = FastHash.GetHashCode("TerrainTextureAtlas");
            uint gameMapHash = FastHash.GetHashCode("GameMap");
            List<Asset> otherAssets = new(manifest.Assets.Where(x => x.InstanceDataSize > 0 && x.TypeId != terrainTextureAtlasHash && x.TypeId != gameScriptListHash && x.TypeId != gameMapHash));
            if (_assets.Count == 0)
            {
                _assets.AddRange(manifest.Assets.Where(x => x.InstanceDataSize > 0).Select(x => new FilterAsset(x)));
                otherAssets.Clear();
                for (int idx = 0; idx < _assets.Count; ++idx)
                {
                    FilterAsset asset = _assets[idx];
                    if (asset.Asset.TypeId == terrainTextureAtlasHash || asset.Asset.TypeId == gameScriptListHash || asset.Asset.TypeId == gameMapHash)
                    {
                        Console.WriteLine($"Discarding asset {asset.Asset.QualifiedName}, manual map specific.");

                        _assets.Remove(asset);
                        --idx;
                    }
                }
            }
            else
            {
                for (int idx = 0; idx < _assets.Count; ++idx)
                {
                    FilterAsset asset = _assets[idx];
                    bool inNewStream = false;
                    foreach (Asset otherAsset in manifest.Assets.Where(x => x.InstanceDataSize > 0))
                    {
                        if (asset.Asset.TypeId != otherAsset.TypeId || asset.Asset.InstanceId != otherAsset.InstanceId)
                        {
                            continue;
                        }

                        inNewStream = true;
                        FilterAsset filterAsset = new(otherAsset);
                        if (asset.InstanceHash != filterAsset.InstanceHash || asset.RelocationHash != filterAsset.RelocationHash || asset.ImportsHash != asset.ImportsHash)
                        {
                            Console.WriteLine($"Discarding asset {asset.Asset.QualifiedName}, different version.");
                            _assets.Remove(asset);
                            --idx;
                        }
                        otherAssets.Remove(asset.Asset);
                        break;
                    }
                    if (!inNewStream)
                    {
                        Console.WriteLine($"Discarding asset {asset.Asset.QualifiedName}, not in all streams.");
                        foreach (FilterAsset filterAsset in _assets)
                        {
                            if (filterAsset.Asset.AssetReferences.Any(x => x.TypeId == asset.Asset.TypeId && x.InstanceId == asset.Asset.InstanceId))
                            {
                                Console.WriteLine($"WARNING: Discarded asset is referenced by {filterAsset.Asset.QualifiedName}.");
                            }
                        }
                        otherAssets.Remove(asset.Asset);
                        _assets.Remove(asset);
                        --idx;
                    }
                }
            }
            foreach (Asset otherAsset in otherAssets)
            {
                Console.WriteLine($"Discarding new asset {otherAsset.QualifiedName}, not in all streams.");
            }
        }
    }

    public bool CommitManifest(string path, string name)
    {
        if (_assets.Count == 0)
        {
            return false;
        }

        string manifestBasePath = Path.Combine(path, name);
        Console.WriteLine($"Comitting manifest '{manifestBasePath}.manifest'.");
        uint allTypesHash = _assets[0].Asset.SourceManifest.AllTypesHash;
        using MemoryStream assetEntryStream = new();
        int assetCount = 0;
        int instanceDataSize = 0;
        int maxInstanceChunkSize = 0;
        int maxRelocationChunkSize = 0;
        int maxImportsChunkSize = 0;
        AssetEntry assetEntry = new();
        NameBuffer nameBuffer = new();
        NameBuffer sourceFileNameBuffer = new();
        ReferencedFileBuffer referenceManifestBuffer = new();
        UInt32Buffer assetReferenceBuffer = new();
        foreach (FilterAsset asset in _assets)
        {
            ++assetCount;
            int length = assetReferenceBuffer.Length;
            foreach (AssetReference assetReference in asset.Asset.AssetReferences)
            {
                assetReferenceBuffer.AddValue(assetReference.TypeId);
                assetReferenceBuffer.AddValue(assetReference.InstanceId);
            }
            instanceDataSize += asset.Asset.InstanceDataSize;
            maxInstanceChunkSize = Math.Max(asset.Asset.InstanceDataSize, maxInstanceChunkSize);
            maxRelocationChunkSize = Math.Max(asset.Asset.RelocationDataSize, maxRelocationChunkSize);
            maxImportsChunkSize = Math.Max(asset.Asset.ImportsDataSize, maxImportsChunkSize);
            assetEntry.TypeId = asset.Asset.TypeId;
            assetEntry.InstanceId = asset.Asset.InstanceId;
            assetEntry.TypeHash = asset.Asset.TypeHash;
            assetEntry.InstanceHash = asset.Asset.InstanceHash;
            assetEntry.AssetReferenceOffset = length;
            assetEntry.AssetReferenceCount = asset.Asset.AssetReferences.Length;
            assetEntry.NameOffset = nameBuffer.AddName(asset.Asset.QualifiedName);
            assetEntry.SourceFileNameOffset = sourceFileNameBuffer.AddName(asset.Asset.Source);
            assetEntry.InstanceDataSize = asset.Asset.InstanceDataSize;
            assetEntry.RelocationDataSize = asset.Asset.RelocationDataSize;
            assetEntry.ImportsDataSize = asset.Asset.ImportsDataSize;
            assetEntry.SaveToStream(assetEntryStream, false);
            asset.Commit(manifestBasePath);
        }
        byte[] buffer = assetEntryStream.GetBuffer();
        using Stream fileStream = new FileStream(manifestBasePath + ".manifest", FileMode.Create, FileAccess.Write, FileShare.None);
        new Manifest.ManifestHeader
        {
            StreamChecksum = 0x1337C0DE,
            AllTypesHash = allTypesHash,
            IsBigEndian = false,
            IsLinked = false,
            Version = 5,
            AssetCount = assetCount,
            TotalInstanceDataSize = instanceDataSize,
            MaxInstanceChunkSize = maxInstanceChunkSize,
            MaxRelocationChunkSize = maxRelocationChunkSize,
            MaxImportsChunkSize = maxImportsChunkSize,
            AssetReferenceBufferSize = assetReferenceBuffer.Length,
            ExternalManifestNameBufferSize = referenceManifestBuffer.Length,
            AssetNameBufferSize = nameBuffer.Length,
            SourceFileNameBufferSize = sourceFileNameBuffer.Length,
        }.SaveToStream(fileStream, false);
        fileStream.Write(buffer.AsSpan()[..(int)assetEntryStream.Length]);
        assetReferenceBuffer.SaveToStream(fileStream, false);
        referenceManifestBuffer.SaveToStream(fileStream);
        nameBuffer.SaveToStream(fileStream);
        sourceFileNameBuffer.SaveToStream(fileStream);
        return true;
    }
}

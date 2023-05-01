using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ExtractPatch.Sage;

internal sealed class Manifest
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ManifestHeader
    {
        public bool IsBigEndian;
        public bool IsLinked;
        public ushort Version;
        public uint StreamChecksum;
        public uint AllTypesHash;
        public int AssetCount;
        public int TotalInstanceDataSize;
        public int MaxInstanceChunkSize;
        public int MaxRelocationChunkSize;
        public int MaxImportsChunkSize;
        public int AssetReferenceBufferSize;
        public int ExternalManifestNameBufferSize;
        public int AssetNameBufferSize;
        public int SourceFileNameBufferSize;

        public void Swap()
        {
        }

        public unsafe void SaveToStream(Stream output, bool isBigEndian)
        {
            if (isBigEndian)
            {
                Swap();
            }
            byte[] buffer = new byte[Unsafe.SizeOf<ManifestHeader>()];
            fixed (ManifestHeader* pThis = &this)
            {
                new UnmanagedMemoryStream((byte*)pThis, buffer.Length).Read(buffer, 0, buffer.Length);
            }
            output.Write(buffer);
            if (isBigEndian)
            {
                Swap();
            }
        }
    }

    private readonly string _directory;
    private readonly string _name;
    private readonly ManifestHeader _header;
    private readonly Asset[] _assets;
    private readonly string? _patchManifest;
    private readonly string[] _externalManifests;

    public bool IsLinked => _header.IsLinked;
    public uint StreamChecksum => _header.StreamChecksum;
    public uint AllTypesHash => _header.AllTypesHash;
    public int AssetCount => _header.AssetCount;
    public Asset[] Assets => _assets;
    public string? PatchManifest => _patchManifest;
    public string[] ExternalManifests => _externalManifests;

    public unsafe Manifest(string path)
    {
        _directory = Path.GetDirectoryName(path)!;
        _name = Path.GetFileNameWithoutExtension(path);
        byte[] data = File.ReadAllBytes(path);
        Span<byte> span = data.AsSpan();
        _header = Unsafe.As<byte, ManifestHeader>(ref data[0]);
        int offset = Unsafe.SizeOf<ManifestHeader>();

        if (!IsLinked)
        {
            throw new InvalidDataException("Only supports linked streams. There shouldn't be any unlinked streams out there.");
        }
        using (Stream stream = File.OpenRead(Path.Combine(_directory, _name + ".bin")))
        {
            BinaryReader reader = new(stream);
            if (reader.ReadUInt32() != StreamChecksum)
            {
                throw new InvalidDataException("Checksum mismatch with instance data.");
            }
        }
        using (Stream stream = File.OpenRead(Path.Combine(_directory, _name + ".relo")))
        {
            BinaryReader reader = new(stream);
            if (reader.ReadUInt32() != StreamChecksum)
            {
                throw new InvalidDataException("Checksum mismatch with relocation data.");
            }
        }
        using (Stream stream = File.OpenRead(Path.Combine(_directory, _name + ".imp")))
        {
            BinaryReader reader = new(stream);
            if (reader.ReadUInt32() != StreamChecksum)
            {
                throw new InvalidDataException("Checksum mismatch with imports data.");
            }
        }

        _assets = new Asset[AssetCount];

        Span<AssetEntry> assetEntries = MemoryMarshal.Cast<byte, AssetEntry>(span.Slice(offset, AssetCount * Unsafe.SizeOf<AssetEntry>()));
        offset += AssetCount * Unsafe.SizeOf<AssetEntry>();
        Span<byte> assetReferenceBuffer = span.Slice(offset, _header.AssetReferenceBufferSize);
        offset += _header.AssetReferenceBufferSize;
        Span<byte> externalManifestNameBuffer = span.Slice(offset, _header.ExternalManifestNameBufferSize);
        offset += _header.ExternalManifestNameBufferSize;
        Span<byte> assetNameBuffer = span.Slice(offset, _header.AssetNameBufferSize);
        offset += _header.AssetNameBufferSize;
        Span<byte> sourceFileNameBuffer = span.Slice(offset, _header.SourceFileNameBufferSize);
        offset += _header.SourceFileNameBufferSize;

        int linkedInstanceOffset = 4;
        int linkedRelocationOffset = 4;
        int linkedImportsOffset = 4;
        for (int idx = 0; idx < AssetCount; ++idx)
        {
            ref AssetEntry assetEntry = ref assetEntries[idx];
            _assets[idx] = new Asset(idx, _name, ref assetEntry, assetNameBuffer, sourceFileNameBuffer, assetReferenceBuffer, this, linkedInstanceOffset, linkedRelocationOffset, linkedImportsOffset);
            linkedInstanceOffset += assetEntry.InstanceDataSize;
            linkedRelocationOffset += assetEntry.RelocationDataSize;
            linkedImportsOffset += assetEntry.ImportsDataSize;
        }

        List<string> externalManifests = new();
        while (!externalManifestNameBuffer.IsEmpty)
        {
            bool isPatch = externalManifestNameBuffer[0] == 2;
            string manifestName = Marshal.PtrToStringAnsi(new nint(Unsafe.AsPointer(ref externalManifestNameBuffer[1])))!;
            externalManifestNameBuffer = externalManifestNameBuffer[(2 + manifestName.Length)..];
            if (isPatch)
            {
                _patchManifest = manifestName;
            }
            else
            {
                externalManifests.Add(manifestName);
            }
        }
        _externalManifests = externalManifests.ToArray();
    }

    public Chunk GetChunk(Asset asset)
    {
        Chunk chunk = new();
        chunk.Allocate(asset.InstanceDataSize, asset.RelocationDataSize, asset.ImportsDataSize);
        using (Stream stream = File.OpenRead(Path.Combine(_directory, _name + ".bin")))
        {
            stream.Seek(asset.LinkedInstanceOffset, SeekOrigin.Begin);
            stream.Read(chunk.InstanceBuffer);
        }
        using (Stream stream = File.OpenRead(Path.Combine(_directory, _name + ".relo")))
        {
            stream.Seek(asset.LinkedRelocationOffset, SeekOrigin.Begin);
            stream.Read(chunk.RelocationBuffer);
        }
        using (Stream stream = File.OpenRead(Path.Combine(_directory, _name + ".imp")))
        {
            stream.Seek(asset.LinkedImportsOffset, SeekOrigin.Begin);
            stream.Read(chunk.ImportsBuffer);
        }
        return chunk;
    }

    public byte[]? GetCData(Asset asset)
    {
        string path = Path.Combine(_directory, asset.CDataPath);

        if (!File.Exists(path))
        {
            return null;
        }
        return File.ReadAllBytes(path);
    }

    public override string ToString()
    {
        return Path.Combine(_directory, _name + ".manifest");
    }
}

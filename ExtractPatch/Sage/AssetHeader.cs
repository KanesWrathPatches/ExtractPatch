using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ExtractPatch.Sage;

[StructLayout(LayoutKind.Sequential)]
public struct AssetHeader
{
    public uint TypeId;
    public uint InstanceId;
    public uint TypeHash;
    public uint InstanceHash;
    public int InstanceDataSize;
    public int RelocationDataSize;
    public int ImportsDataSize;
    public uint Zero;

    public void Swap()
    {
    }

    public unsafe void SaveToStream(Stream output, bool isBigEndian)
    {
        if (isBigEndian)
        {
            Swap();
        }
        byte[] buffer = new byte[Unsafe.SizeOf<AssetHeader>()];
        fixed (AssetHeader* pThis = &this)
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

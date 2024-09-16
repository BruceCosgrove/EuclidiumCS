using Euclidium.Core;
using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Euclidium.Rendering;

public readonly struct IndexBufferInfo(DrawElementsType type, int count, BufferUsageARB usage)
{
    public DrawElementsType Type => type;
    public int Count => count;
    public BufferUsageARB Usage => usage;
}

public class IndexBuffer
{
    private readonly DrawElementsType _type;
    private readonly int _count;
    private readonly uint _id;

    public DrawElementsType Type => _type;
    public uint Count => (uint)_count;

    public IndexBuffer(IndexBufferInfo info)
    {
        var vk = Engine.Instance.Window.VK;

        _type = info.Type;
        _count = info.Count;
        //_id = vk.CreateBuffer();
        nuint size = GetSize(_type, info.Count);
        //vk.BindBuffer(BufferTargetARB.ElementArrayBuffer, _id);
        //vk.BufferData(BufferTargetARB.ElementArrayBuffer, size, ReadOnlySpan<byte>.Empty, info.Usage);
    }

    public void Destroy()
    {
        var vk = Engine.Instance.Window.VK;

        //vk.DeleteBuffer(_id);
    }

    public void Bind()
    {
        var vk = Engine.Instance.Window.VK;

        //vk.BindBuffer(BufferTargetARB.ElementArrayBuffer, _id);
    }

    public void SetData(List<byte> data) => SetData(data, data.Count);

    public void SetData(List<byte> data, int count, uint offset = 0)
    {
        Debug.Assert(_type == DrawElementsType.UnsignedByte);
        Debug.Assert(offset + count <= _count);
        Debug.Assert(0 < count && count <= data.Count);

        var vk = Engine.Instance.Window.VK;

        ReadOnlySpan<byte> span = CollectionsMarshal.AsSpan(data);
        //vk.NamedBufferSubData(_id, (nint)offset, GetSize(_type, count), span);
    }

    public void SetData(List<ushort> data) => SetData(data, data.Count);

    public void SetData(List<ushort> data, int count, uint offset = 0)
    {
        Debug.Assert(_type == DrawElementsType.UnsignedShort);
        Debug.Assert(offset + count <= _count);
        Debug.Assert(0 < count && count <= data.Count);

        var vk = Engine.Instance.Window.VK;

        ReadOnlySpan<ushort> span = CollectionsMarshal.AsSpan(data);
        //vk.NamedBufferSubData(_id, (nint)offset, GetSize(_type, count), span);
    }

    public void SetData(List<uint> data) => SetData(data, data.Count);

    public void SetData(List<uint> data, int count, uint offset = 0)
    {
        Debug.Assert(_type == DrawElementsType.UnsignedInt);
        Debug.Assert(offset + count <= _count);
        Debug.Assert(0 < count && count <= data.Count);

        var vk = Engine.Instance.Window.VK;

        ReadOnlySpan<uint> span = CollectionsMarshal.AsSpan(data);
        //vk.NamedBufferSubData(_id, (nint)offset, GetSize(_type, count), span);
    }

    private static readonly Dictionary<DrawElementsType, int> s_sizes;

    static IndexBuffer()
    {
        s_sizes = new()
        {
            [DrawElementsType.UnsignedByte]  = sizeof(byte),
            [DrawElementsType.UnsignedShort] = sizeof(ushort),
            [DrawElementsType.UnsignedInt]   = sizeof(uint),
        };
    }

    private static nuint GetSize(DrawElementsType type, int count) => (nuint)(count * s_sizes[type]);
}

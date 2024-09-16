using Euclidium.Core;
using Silk.NET.OpenGL;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Euclidium.Rendering;

public enum VertexBufferElementType
{
    Float,
    Float2,
    Float3,
    Float4,

    Int,
    Int2,
    Int3,
    Int4,

    UInt,
    UInt2,
    UInt3,
    UInt4,
}

public struct VertexBufferElement
{
    public readonly VertexAttribPointerType Type;
    public readonly uint Count;
    public readonly uint Size;
    public readonly bool Normalized;
    public uint Offset = 0; // To be calculated in VertexBufferLayout constructor.

    public VertexBufferElement(VertexBufferElementType type, bool normalized = false)
    {
        var conversion = s_conversions[type];
        Type = conversion.Type;
        Count = conversion.Count;
        Size = conversion.Size;
        Normalized = normalized;
    }

    private readonly struct Conversion(VertexAttribPointerType type, uint count, uint size)
    {
        public VertexAttribPointerType Type => type;
        public uint Count => count;
        public uint Size => size;
    }

    private static readonly Dictionary<VertexBufferElementType, Conversion> s_conversions;

    static VertexBufferElement()
    {
        s_conversions = new()
        {
            [VertexBufferElementType.Float]  = new(VertexAttribPointerType.Float,       1, 1 * sizeof(float)),
            [VertexBufferElementType.Float2] = new(VertexAttribPointerType.Float,       2, 2 * sizeof(float)),
            [VertexBufferElementType.Float3] = new(VertexAttribPointerType.Float,       3, 3 * sizeof(float)),
            [VertexBufferElementType.Float4] = new(VertexAttribPointerType.Float,       4, 4 * sizeof(float)),
            [VertexBufferElementType.Int]    = new(VertexAttribPointerType.Int,         1, 1 * sizeof(int)),
            [VertexBufferElementType.Int2]   = new(VertexAttribPointerType.Int,         2, 2 * sizeof(int)),
            [VertexBufferElementType.Int3]   = new(VertexAttribPointerType.Int,         3, 3 * sizeof(int)),
            [VertexBufferElementType.Int4]   = new(VertexAttribPointerType.Int,         4, 4 * sizeof(int)),
            [VertexBufferElementType.UInt]   = new(VertexAttribPointerType.UnsignedInt, 1, 1 * sizeof(uint)),
            [VertexBufferElementType.UInt2]  = new(VertexAttribPointerType.UnsignedInt, 2, 2 * sizeof(uint)),
            [VertexBufferElementType.UInt3]  = new(VertexAttribPointerType.UnsignedInt, 3, 3 * sizeof(uint)),
            [VertexBufferElementType.UInt4]  = new(VertexAttribPointerType.UnsignedInt, 4, 4 * sizeof(uint)),
        };
    }
}

public readonly struct VertexBufferLayout
{
    public readonly List<VertexBufferElement> Elements;
    public readonly uint Stride;

    public VertexBufferLayout(List<VertexBufferElement> elements)
    {
        uint stride = 0;
        Elements = elements.ConvertAll(element =>
        {
            element.Offset = stride;
            stride += element.Size;
            return element;
        });
        Stride = stride;
    }
}

public readonly struct VertexBufferInfo(VertexBufferLayout layout, int count, BufferUsageARB usage)
{
    public VertexBufferLayout Layout => layout;
    public int Count => count;
    public BufferUsageARB Usage => usage;
}

// This implementation also contains an embedded vertex array.
// I've chosen this because I only ever want to support single
// vertex buffers with interwoven data per vertex array.
public class VertexBuffer
{
    private readonly uint _id;
    private readonly uint _idVAO;
    private readonly int _count;
    private readonly VertexBufferLayout _layout;

    public int Count => _count;
    public VertexBufferLayout Layout => _layout;

    public VertexBuffer(VertexBufferInfo info)
    {
        //var vk = Engine.Instance.Window.VK;

        _count = info.Count;
        _layout = info.Layout;

        //_idVAO = vk.CreateVertexArray();
        //vk.BindVertexArray(_idVAO);

        nuint size = (nuint)(info.Count * sizeof(float));
        //_id = vk.CreateBuffer();
        //vk.BindBuffer(BufferTargetARB.ArrayBuffer, _id);
        //vk.BufferData(BufferTargetARB.ArrayBuffer, size, ReadOnlySpan<byte>.Empty, info.Usage);

        for (uint i = 0; i < _layout.Elements.Count; ++i)
        {
            //vk.EnableVertexAttribArray(i);
            var element = _layout.Elements[(int)i];
            var count = (int)element.Count;
            unsafe
            {
                var offset = (void*)element.Offset;
                switch (element.Type)
                {
                    case VertexAttribPointerType.Float:
                        //vk.VertexAttribPointer(i, count, element.Type, element.Normalized, _layout.Stride, offset);
                        break;
                    case VertexAttribPointerType.Int:
                        //vk.VertexAttribIPointer(i, count, GLEnum.Int, _layout.Stride, offset);
                        break;
                    case VertexAttribPointerType.UnsignedInt:
                        //vk.VertexAttribIPointer(i, count, GLEnum.UnsignedInt, _layout.Stride, offset);
                        break;
                }
            }
        }
    }
    public void Destroy()
    {
        //var vk = Engine.Instance.Window.VK;

        //vk.DeleteBuffer(_id);
        //vk.DeleteVertexArray(_idVAO);
    }

    public void Bind()
    {
        //var vk = Engine.Instance.Window.VK;

        //vk.BindVertexArray(_idVAO);
        //vk.BindBuffer(BufferTargetARB.ArrayBuffer, _id);
    }

    public void SetData(List<float> data) => SetData(data, data.Count);

    public void SetData(List<float> data, int count, uint offset = 0)
    {
        Debug.Assert(offset + count <= _count);
        Debug.Assert(0 < count && count <= data.Count);

        //var vk = Engine.Instance.Window.VK;

        ReadOnlySpan<float> span = CollectionsMarshal.AsSpan(data);
        //vk.NamedBufferSubData(_id, (nint)offset, (nuint)(count * sizeof(float)), span);
    }
}

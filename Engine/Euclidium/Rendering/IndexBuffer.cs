using Euclidium.Core;
using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

public enum IndexBufferType
{
    UInt16 = IndexType.Uint16,
    UInt32 = IndexType.Uint32,
}

public class StaticIndexBuffer : StaticBuffer
{
    private IndexBufferType _type;

    public override void Bind() => IndexBuffer.Bind(_buffer, (IndexType)_type);

    public void Create(ulong size, IndexBufferType type)
    {
        Create(size, BufferUsageFlags.IndexBufferBit);
        _type = type;
    }
}

public class DynamicIndexBuffer : DynamicBuffer
{
    private IndexBufferType _type;

    public override void Bind() => IndexBuffer.Bind(_buffer, (IndexType)_type);

    public void Create(ulong size, IndexBufferType type)
    {
        Create(size, BufferUsageFlags.IndexBufferBit);
        _type = type;
    }
}

file class IndexBuffer
{
    internal static unsafe void Bind(Silk.NET.Vulkan.Buffer buffer, IndexType type)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var commandBuffer = context.CommandBuffer;

        vk.CmdBindIndexBuffer(commandBuffer, buffer, 0, type);
    }
}

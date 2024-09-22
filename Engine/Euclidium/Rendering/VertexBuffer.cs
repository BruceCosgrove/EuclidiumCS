using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

public sealed class StaticVertexBuffer : StaticBuffer
{
    public unsafe void Create(ulong size) =>
        Create(size, BufferUsageFlags.VertexBufferBit);
}

public sealed class DynamicVertexBuffer : DynamicBuffer
{
    public unsafe void Create(ulong size) =>
        Create(size, BufferUsageFlags.VertexBufferBit);
}

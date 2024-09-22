using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

public sealed class VertexBuffer : Buffer
{
    public unsafe void Create(ulong size) =>
        Create(size, BufferUsageFlags.VertexBufferBit);
}

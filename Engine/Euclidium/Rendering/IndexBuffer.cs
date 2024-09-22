using Euclidium.Core;
using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

public enum IndexBufferType
{
    UInt16 = IndexType.Uint16,
    UInt32 = IndexType.Uint32,
}

public sealed class IndexBuffer : IBuffer
{
    private Buffer? _buffer;
    private IndexBufferType _type;

    public ulong Size => _buffer!.Size;
    public IndexBufferType Type => _type;

    public void Create(ulong size, BufferUsage usage, IndexBufferType type)
    {
        _buffer = Buffer.Select(usage);
        _buffer.Create(size, BufferUsageFlags.IndexBufferBit);
        _type = type;
    }

    public void Dispose() => _buffer!.Dispose();

    public void Bind()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var commandBuffer = context.CommandBuffer;

        vk.CmdBindIndexBuffer(commandBuffer, _buffer!.Handle, 0, (IndexType)_type);
    }

    public unsafe void SetData(void* data, ulong size, ulong offset = 0) =>
        _buffer!.SetData(data, size, offset);
}

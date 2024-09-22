using Euclidium.Core;
using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

public sealed class VertexBuffer : IBuffer
{
    private Buffer? _buffer;

    public ulong Size => _buffer!.Size;

    public void Create(ulong size, BufferUsage usage)
    {
        _buffer = Buffer.Select(usage);
        _buffer.Create(size, BufferUsageFlags.VertexBufferBit);
    }

    public void Dispose() => _buffer!.Dispose();

    public unsafe void Bind()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var commandBuffer = context.CommandBuffer;

        var buffer = _buffer!.Handle;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &buffer, &offset);
    }

    public unsafe void SetData(void* data, ulong size, ulong offset = 0) =>
        _buffer!.SetData(data, size, offset);
}

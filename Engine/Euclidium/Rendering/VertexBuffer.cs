using Euclidium.Core;
using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

public sealed class StaticVertexBuffer : StaticBuffer
{
    public override void Bind() => VertexBuffer.Bind(_buffer);

    public void Create(ulong size) => Create(size, BufferUsageFlags.VertexBufferBit);
}

public sealed class DynamicVertexBuffer : DynamicBuffer
{
    public override void Bind() => VertexBuffer.Bind(_buffer);

    public void Create(ulong size) => Create(size, BufferUsageFlags.VertexBufferBit);
}

file class VertexBuffer
{
    internal static unsafe void Bind(Silk.NET.Vulkan.Buffer buffer)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var commandBuffer = context.CommandBuffer;

        ulong offset = 0;
        vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &buffer, &offset);
    }
}

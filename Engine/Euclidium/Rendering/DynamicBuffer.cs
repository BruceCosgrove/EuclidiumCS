using Euclidium.Core;
using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

public abstract class DynamicBuffer : Buffer
{
    protected void Create(ulong size, BufferUsageFlags usage)
    {
        try
        {
            (_buffer, _memory) = CreateBuffer(
                size,
                usage,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );
        }
        catch
        {
            Dispose();
            throw;
        }

        _size = size;
    }

    public override unsafe void Dispose()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        RenderHelper.Dispose(ref _memory, handle => vk.FreeMemory(device, handle, null));
        RenderHelper.Dispose(ref _buffer, handle => vk.DestroyBuffer(device, handle, null));
    }

    public override unsafe void SetData(void* data, ulong size, ulong offset = 0) =>
        CopyData(data, size, offset, _memory, false);
}

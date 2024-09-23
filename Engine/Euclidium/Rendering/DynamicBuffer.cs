using Euclidium.Core;
using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

internal unsafe class DynamicBuffer : Buffer
{
    private nint _mappedMemory;

    public override void Create(ulong size, BufferUsageFlags usage)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        try
        {
            (_buffer, _memory, _size) = CreateBuffer(
                size,
                usage,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );

            void* mappedMemory;
            RenderHelper.Require(vk.MapMemory(device, _memory, 0, _size, MemoryMapFlags.None, &mappedMemory));
            _mappedMemory = (nint)mappedMemory;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override unsafe void Dispose()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        RenderHelper.Dispose(ref _mappedMemory, handle => vk.UnmapMemory(device, _memory));
        RenderHelper.Dispose(ref _memory, handle => vk.FreeMemory(device, handle, null));
        RenderHelper.Dispose(ref _buffer, handle => vk.DestroyBuffer(device, handle, null));
    }

    public override unsafe void SetData(void* data, ulong size, ulong offset = 0)
    {
        AssertSizeOffsetBounded(size, offset);
        System.Buffer.MemoryCopy(data, (void*)_mappedMemory, size, size);
    }
}

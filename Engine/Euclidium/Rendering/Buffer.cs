using Euclidium.Core;
using Silk.NET.Vulkan;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Euclidium.Rendering;

// A temporary staging buffer is good for buffers whose contents don't change,
// e.g. a complex 3D model. However, for dynamic buffers, e.g. in my typical
// batch renderers, the buffers are recreated each frame, which I believe
// would instead lose performance from the extra step of the staging buffer,
// including ImGui. Hence, both types are provided.
public enum BufferUsage
{
    Static,
    Dynamic,
}

public interface IBuffer : IDisposable
{
    void Bind();

    unsafe void SetData(void* data, ulong size, ulong offset = 0);
}

internal abstract class Buffer : IDisposable
{
    protected Silk.NET.Vulkan.Buffer _buffer;
    protected DeviceMemory _memory;
    protected ulong _size;

    internal Silk.NET.Vulkan.Buffer Handle => _buffer;
    internal ulong Size => _size;

    public static Buffer Select(BufferUsage usage)
    {
        return usage switch
        {
            BufferUsage.Static => new StaticBuffer(),
            BufferUsage.Dynamic => new DynamicBuffer(),
            _ => throw new Exception("Unknown buffer usage."),
        };
    }

    public abstract void Create(ulong size, BufferUsageFlags usage);

    public abstract void Dispose();

    public abstract unsafe void SetData(void* data, ulong size, ulong offset = 0);

    protected unsafe void CopyData(void* data, ulong size, ulong offset, DeviceMemory dstMemory, bool flush)
    {
        Debug.Assert(0 < size && size <= _size);
        Debug.Assert(offset < _size);
        Debug.Assert(offset + size <= _size);

        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        // Copy the memory.
        void* dstMemoryPtr;
        RenderHelper.Require(vk.MapMemory(device, dstMemory, offset, size, MemoryMapFlags.None, &dstMemoryPtr));
        System.Buffer.MemoryCopy(data, dstMemoryPtr, size, size);

        if (flush)
        {
            // Tell vulkan to use the new memory instead of cached memory.
            MappedMemoryRange mappedMemoryRange = new()
            {
                SType = StructureType.MappedMemoryRange,
                Memory = dstMemory,
                Offset = offset,
                Size = size,
            };

            RenderHelper.Require(
                vk.FlushMappedMemoryRanges(device, 1, &mappedMemoryRange),
                "Failed to flush mapped memory range for buffer."
            );
        }

        vk.UnmapMemory(device, dstMemory);
    }

    protected static unsafe (Silk.NET.Vulkan.Buffer, DeviceMemory) CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        Silk.NET.Vulkan.Buffer buffer = new();
        DeviceMemory memory = new();

        try
        {
            BufferCreateInfo bufferCreateInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };

            RenderHelper.Require(
                vk.CreateBuffer(device, &bufferCreateInfo, null, out buffer),
                "Failed to create buffer."
            );

            var memoryRequirements = vk.GetBufferMemoryRequirements(device, buffer);
            var memoryTypeIndex = SelectMemoryType(memoryRequirements.MemoryTypeBits, properties);

            MemoryAllocateInfo memoryAllocateInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memoryRequirements.Size,
                MemoryTypeIndex = memoryTypeIndex,
            };

            RenderHelper.Require(
                vk.AllocateMemory(device, &memoryAllocateInfo, null, out memory),
                "Failed to allocate buffer memory."
            );

            RenderHelper.Require(
                vk.BindBufferMemory(device, buffer, memory, 0),
                "Failed to bind buffer memory."
            );
        }
        catch
        {
            RenderHelper.Dispose(ref memory, handle => vk.FreeMemory(device, handle, null));
            RenderHelper.Dispose(ref buffer, handle => vk.DestroyBuffer(device, handle, null));
            throw;
        }

        return (buffer, memory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe uint SelectMemoryType(uint typeBits, MemoryPropertyFlags propertyFlags)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var physicalDevice = context.PhysicalDevice;

        var physicalDeviceMemoryProperties = vk.GetPhysicalDeviceMemoryProperties(physicalDevice);
        var memoryTypes = physicalDeviceMemoryProperties.MemoryTypes.AsSpan();

        for (int i = 0; i < memoryTypes.Length; ++i)
            if ((typeBits & (1 << i)) != 0 && (memoryTypes[i].PropertyFlags & propertyFlags) == propertyFlags)
                return (uint)i;

        throw new Exception("Failed to find suitable buffer memory type.");
    }
}

using Euclidium.Core;
using Silk.NET.Vulkan;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Euclidium.Rendering;

public sealed class VertexBuffer : IDisposable
{
    private Silk.NET.Vulkan.Buffer _buffer;
    private DeviceMemory _memory;

    private ulong _size;

    public ulong Size => _size;

    public unsafe void Create(ulong size)
    {
        try
        {
            var context = Engine.Instance.Window.Context;
            var vk = context.VK;
            var device = context.Device;

            BufferCreateInfo bufferCreateInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = size,
                Usage = BufferUsageFlags.VertexBufferBit,
                SharingMode = SharingMode.Exclusive,
            };

            RenderHelper.Require(
                vk.CreateBuffer(device, &bufferCreateInfo, null, out _buffer),
                "Failed to create vertex buffer."
            );

            var memoryRequirements = vk.GetBufferMemoryRequirements(device, _buffer);
            var memoryTypeIndex = SelectMemoryType(
                memoryRequirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );

            MemoryAllocateInfo memoryAllocateInfo = new()
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = memoryRequirements.Size,
                MemoryTypeIndex = memoryTypeIndex,
            };

            RenderHelper.Require(
                vk.AllocateMemory(device, &memoryAllocateInfo, null, out _memory),
                "Failed to allocate memory for vertex buffer."
            );

            RenderHelper.Require(
                vk.BindBufferMemory(device, _buffer, _memory, 0),
                "Failed to bind memory to vertex buffer."
            );
        }
        catch
        {
            Dispose();
            throw;
        }

        _size = size;
    }

    public unsafe void Dispose()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        RenderHelper.Dispose(ref _memory, handle => vk.FreeMemory(device, handle, null));
        RenderHelper.Dispose(ref _buffer, handle => vk.DestroyBuffer(device, handle, null));
    }

    public unsafe void Bind()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var commandBuffer = context.CommandBuffer;

        var buffer = _buffer;
        ulong offset = 0;
        vk.CmdBindVertexBuffers(commandBuffer, 0, 1, &buffer, &offset);
    }

    public unsafe void SetData(void* data, ulong size, ulong offset = 0)
    {
        Debug.Assert(0 < size && size <= _size);
        Debug.Assert(offset < _size);
        Debug.Assert(offset + size <= _size);

        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        void* bufferMemory;
        vk.MapMemory(device, _memory, offset, size, MemoryMapFlags.None, &bufferMemory);
        System.Buffer.MemoryCopy(data, bufferMemory, size, size);
        vk.UnmapMemory(device, _memory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe uint SelectMemoryType(uint typeBits, MemoryPropertyFlags propertyFlags)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var physicalDevice = context.PhysicalDevice;

        var physicalDeviceMemoryProperties = vk.GetPhysicalDeviceMemoryProperties(physicalDevice);
        var memoryTypes = physicalDeviceMemoryProperties.MemoryTypes.AsSpan();

        for (int i = 0; i < memoryTypes.Length; ++i)
            if ((typeBits & (1 << i)) != 0 && (memoryTypes[i].PropertyFlags & propertyFlags) == propertyFlags)
                return (uint)i;

        throw new Exception("Failed to find suitable memory type for vertex buffer.");
    }
}

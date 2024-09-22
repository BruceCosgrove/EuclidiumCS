using Euclidium.Core;
using Silk.NET.Vulkan;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Euclidium.Rendering;

public abstract class Buffer : IDisposable
{
    private Silk.NET.Vulkan.Buffer _stagingBuffer;
    private DeviceMemory _stagingMemory;
    private Silk.NET.Vulkan.Buffer _buffer;
    private DeviceMemory _memory;

    private ulong _size;

    public ulong Size => _size;

    protected void Create(ulong size, BufferUsageFlags usage)
    {
        try
        {
            (_stagingBuffer, _stagingMemory) = CreateBuffer(
                size,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );
            (_buffer, _memory) = CreateBuffer(
                size,
                BufferUsageFlags.TransferDstBit | usage,
                MemoryPropertyFlags.DeviceLocalBit
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
        RenderHelper.Dispose(ref _stagingMemory, handle => vk.FreeMemory(device, handle, null));
        RenderHelper.Dispose(ref _stagingBuffer, handle => vk.DestroyBuffer(device, handle, null));
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
        var commandPool = context.CommandPool;
        var graphicsQueue = context.GraphicsQueue;

        void* stagingMemory;
        RenderHelper.Require(vk.MapMemory(device, _stagingMemory, offset, size, MemoryMapFlags.None, &stagingMemory));
        System.Buffer.MemoryCopy(data, stagingMemory, size, size);
        vk.UnmapMemory(device, _stagingMemory);

        CommandBufferAllocateInfo commandBufferAllocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };

        RenderHelper.Require(
            vk.AllocateCommandBuffers(device, &commandBufferAllocateInfo, out var commandBuffer),
            "Failed to allocate command buffer for buffer transfer operations."
        );

        CommandBufferBeginInfo commandBufferBeginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };

        RenderHelper.Require(
            vk.BeginCommandBuffer(commandBuffer, &commandBufferBeginInfo),
            "Failed to begin command buffer for buffer transfer operations."
        );

        BufferCopy bufferCopy = new()
        {
            SrcOffset = offset,
            DstOffset = offset,
            Size = size,
        };
        vk.CmdCopyBuffer(commandBuffer, _stagingBuffer, _buffer, 1, &bufferCopy);

        RenderHelper.Require(
            vk.EndCommandBuffer(commandBuffer),
            "Failed to end command buffer for buffer transfer operations."
        );

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
        };

        RenderHelper.Require(
            vk.QueueSubmit(graphicsQueue, 1, &submitInfo, default),
            "Failed to submit command buffer for buffer transfer operations."
        );

        RenderHelper.Require(vk.QueueWaitIdle(graphicsQueue));

        vk.FreeCommandBuffers(device, commandPool, 1, &commandBuffer);
    }

    private static unsafe (Silk.NET.Vulkan.Buffer, DeviceMemory) CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        Silk.NET.Vulkan.Buffer buffer = default;
        DeviceMemory memory = default;

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

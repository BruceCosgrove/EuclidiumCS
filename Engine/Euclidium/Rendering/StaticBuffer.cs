using Euclidium.Core;
using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

internal class StaticBuffer : Buffer
{
    public override void Create(ulong size, BufferUsageFlags usage)
    {
        try
        {
            (_buffer, _memory, _size) = CreateBuffer(
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
    }

    public override unsafe void Dispose()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        RenderHelper.Dispose(ref _memory, handle => vk.FreeMemory(device, handle, null));
        RenderHelper.Dispose(ref _buffer, handle => vk.DestroyBuffer(device, handle, null));
    }

    public override unsafe void SetData(void* data, ulong size, ulong offset = 0)
    {
        AssertSizeOffsetBounded(size, offset);

        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;
        var commandPool = context.CommandPool;
        var graphicsQueue = context.GraphicsQueue;

        Silk.NET.Vulkan.Buffer stagingBuffer = new();
        DeviceMemory stagingMemory = new();
        CommandBuffer commandBuffer = new();

        try
        {
            (stagingBuffer, stagingMemory, _) = CreateBuffer(
                size,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit
            );

            // Copy the memory.
            void* mappedMemory;
            RenderHelper.Require(vk.MapMemory(device, stagingMemory, offset, size, MemoryMapFlags.None, &mappedMemory));
            System.Buffer.MemoryCopy(data, mappedMemory, size, size);

            // Tell vulkan to use the new memory instead of cached memory.
            MappedMemoryRange mappedMemoryRange = new()
            {
                SType = StructureType.MappedMemoryRange,
                Memory = stagingMemory,
                Offset = offset,
                Size = size,
            };
            RenderHelper.Require(
                vk.FlushMappedMemoryRanges(device, 1, &mappedMemoryRange),
                "Failed to flush mapped memory range for buffer."
            );

            vk.UnmapMemory(device, stagingMemory);

            CommandBufferAllocateInfo commandBufferAllocateInfo = new()
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };

            RenderHelper.Require(
                vk.AllocateCommandBuffers(device, &commandBufferAllocateInfo, out commandBuffer),
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
            vk.CmdCopyBuffer(commandBuffer, stagingBuffer, _buffer, 1, &bufferCopy);

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
        }
        finally
        {
            vk.FreeCommandBuffers(device, commandPool, 1, &commandBuffer);
            RenderHelper.Dispose(ref stagingMemory, handle => vk.FreeMemory(device, handle, null));
            RenderHelper.Dispose(ref stagingBuffer, handle => vk.DestroyBuffer(device, handle, null));
        }
    }
}

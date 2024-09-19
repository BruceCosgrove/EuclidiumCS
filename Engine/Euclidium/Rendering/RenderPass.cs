using Euclidium.Core;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;

namespace Euclidium.Rendering;

public sealed class RenderPass : IDisposable
{
    private Silk.NET.Vulkan.RenderPass _renderPass;

    internal Silk.NET.Vulkan.RenderPass Handle => _renderPass;

    public RenderPass(
        AttachmentDescription[] attachmentDescriptions,
        AttachmentReference[] colorAttachments
    ) {
        try
        {
            // Create the render pass.
            CreateRenderPass(attachmentDescriptions, colorAttachments);
        }
        catch
        {
            Dispose(); // Dispose what was partially created.
            throw;
        }
    }

    public unsafe void Dispose()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        DisposeHelper.Dispose(ref _renderPass, handle => vk.DestroyRenderPass(device, handle, null));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateRenderPass(
        AttachmentDescription[] attachmentDescriptions,
        AttachmentReference[] colorAttachments
    ) {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        fixed (AttachmentDescription* attachmentDescriptionsPtr = attachmentDescriptions)
        fixed (AttachmentReference* colorAttachmentsPtr = colorAttachments)
        {
            SubpassDescription subpassDescription = new()
            {
                PipelineBindPoint = PipelineBindPoint.Graphics, // TODO: longterm, e.g. compute shaders, raytracing
                ColorAttachmentCount = (uint)colorAttachments.Length,
                PColorAttachments = colorAttachmentsPtr,
                // TODO: 6 other parameters
            };

            RenderPassCreateInfo renderPassCreateInfo = new()
            {
                SType = StructureType.RenderPassCreateInfo,
                AttachmentCount = (uint)attachmentDescriptions.Length,
                PAttachments = attachmentDescriptionsPtr,
                SubpassCount = 1, // TODO
                PSubpasses = &subpassDescription, // TODO
                // TODO: 2 other parameters
            };

            if (vk.CreateRenderPass(device, &renderPassCreateInfo, null, out _renderPass) != Result.Success)
                throw new Exception("Failed to create render pass.");
        }
    }
}

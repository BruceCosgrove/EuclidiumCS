using Silk.NET.Core.Native;
using Silk.NET.Core;
using Silk.NET.Vulkan;
#if DEBUG
using Silk.NET.Vulkan.Extensions.EXT;
#endif
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Euclidium.Rendering;

public sealed class GraphicsContext : IDisposable
{
    private struct QueueFamilySupport
    {
        public uint? GraphicsFamily;
        public uint? PresentFamily;

        public readonly bool IsComplete => GraphicsFamily.HasValue && PresentFamily.HasValue;
    }

    private struct SwapChainSupport
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    private static readonly string[] s_requiredInstanceLayers =
    [
#if DEBUG
        "VK_LAYER_KHRONOS_validation",
#endif
    ];

    private static readonly string[] s_requiredDeviceExtensions =
    [
        KhrSwapchain.ExtensionName,
    ];

    private const uint FramesInFlight = 2;

    private Vk? _vk;
    private Instance _instance;
#if DEBUG
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;
#endif
    private KhrSurface? _khrSurface;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _presentationQueue;
    private KhrSwapchain? _khrSwapchain;
    private SwapchainKHR _swapchain;
    private Image[]? _swapchainImages;
    private ImageView[]? _swapchainImageViews;
    private Silk.NET.Vulkan.Framebuffer[]? _swapchainFramebuffers;
    private RenderPass? _renderPass;
    private CommandPool _commandPool;
    private CommandBuffer[]? _commandBuffers;
    private Silk.NET.Vulkan.Semaphore[]? _imageAvailableSemaphores;
    private Silk.NET.Vulkan.Semaphore[]? _frameFinishedSemaphores;
    private Fence[]? _frameInFlightFences;

    private QueueFamilySupport _queueFamilySupport;
    private SwapChainSupport _swapchainSupport;
    private SurfaceFormatKHR _swapchainImageFormat;
    private PresentModeKHR _swapchainPresentMode;
    private Extent2D _swapchainImageExtent;
    private uint _swapchainImageCount;
    private uint _swapchainImageIndex;
    private uint _currentFrameInFlightIndex;

    public Vk VK => _vk!;
    public Instance Instance => _instance!;
    public Device Device => _device!;
    public Extent2D SwapchainImageExtent => _swapchainImageExtent!;
    public RenderPass RenderPass => _renderPass!;
    public CommandBuffer CommandBuffer => _commandBuffers![_currentFrameInFlightIndex];

    public unsafe void Create(IWindow window)
    {
        // Get list of extensions for the instance.
        // NOTE: this is here because the below allocations
        // of unmanaged objects require it; those in turn
        // need to be declared before the try block.
        var surfaceExtensions = window.VkSurface!.GetRequiredExtensions(out uint surfaceExtensionCount);
        var instanceExtensions = SilkMarshal.PtrToStringArray((nint)surfaceExtensions, (int)surfaceExtensionCount);
#if DEBUG
        instanceExtensions = [..instanceExtensions, ExtDebugUtils.ExtensionName];
#endif

        // Allocate a few unmanaged objects.
        var applicationName = (byte*)SilkMarshal.StringToPtr("EuclidiumCS");
        var engineName = (byte*)SilkMarshal.StringToPtr("Euclidium");
        var instanceExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(instanceExtensions);
        var deviceExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(s_requiredDeviceExtensions);
#if DEBUG
        var layerNames = (byte**)SilkMarshal.StringArrayToPtr(s_requiredInstanceLayers);
#endif

        try
        {
            CreateAPI(window);
            CreateInstance(applicationName, engineName, instanceExtensions.Length, instanceExtensionNames
#if DEBUG
                , layerNames
#endif
            );
            CreateSurface(window);

            SelectPhysicalDevice();
            CreateDevice(deviceExtensionNames
#if DEBUG
                , layerNames
#endif
            );
            // TODO: Currently, the swapchain image format is assumed to not change.
            // However it may, in which case, the render pass, and anything that
            // required it to be created, must be recreated, e.g. a shader pipeline.
            _swapchainImageFormat = SelectSurfaceFormat();
            CreateRenderPass();
            CreateSwapchain();
            CreateCommandPool();
            CreateCommandBuffers();
            CreateSynchronizationObjects();
        }
        catch
        {
            Dispose(); // Dispose what was partially created.
            throw;
        }
        finally
        {
            // Deallocate all the unmanaged objects, regardless of exceptions.
#if DEBUG
            SilkMarshal.Free((nint)layerNames);
#endif
            SilkMarshal.Free((nint)deviceExtensionNames);
            SilkMarshal.Free((nint)instanceExtensionNames);
            SilkMarshal.Free((nint)engineName);
            SilkMarshal.Free((nint)applicationName);
        }
    }

    // Disposes any created unmanaged resources in the reverse order they were created in.
    // This even works if it was only partially created or already partially or entirely disposed.
    public unsafe void Dispose()
    {
        DisposeHelper.Dispose(ref _frameInFlightFences, handle => _vk!.DestroyFence(_device, handle, null));
        DisposeHelper.Dispose(ref _frameFinishedSemaphores, handle => _vk!.DestroySemaphore(_device, handle, null));
        DisposeHelper.Dispose(ref _imageAvailableSemaphores, handle => _vk!.DestroySemaphore(_device, handle, null));
        DisposeHelper.Dispose(ref _commandPool, handle => _vk!.DestroyCommandPool(_device, handle, null));
        DisposeSwapchain();
        DisposeHelper.Dispose(ref _khrSwapchain); // Don't dispose the extension API with the swapchain.
        DisposeHelper.Dispose(ref _renderPass);
        DisposeHelper.Dispose(ref _device, handle => _vk!.DestroyDevice(handle, null));
        DisposeHelper.Dispose(ref _surface, handle => _khrSurface!.DestroySurface(_instance, handle, null));
        DisposeHelper.Dispose(ref _khrSurface);
#if DEBUG
        DisposeHelper.Dispose(ref _debugMessenger, handle => _debugUtils!.DestroyDebugUtilsMessenger(_instance, handle, null));
        DisposeHelper.Dispose(ref _debugUtils);
#endif
        DisposeHelper.Dispose(ref _instance, handle => _vk!.DestroyInstance(handle, null));
        DisposeHelper.Dispose(ref _vk);
    }

    private unsafe void DisposeSwapchain()
    {
        DisposeHelper.Dispose(ref _swapchainFramebuffers, handle => _vk!.DestroyFramebuffer(_device, handle, null));
        DisposeHelper.Dispose(ref _swapchainImageViews, handle => _vk!.DestroyImageView(_device, handle, null));
        DisposeHelper.Dispose(ref _swapchain, handle => _khrSwapchain!.DestroySwapchain(_device, handle, null));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CreateAPI(IWindow window)
    {
        _vk = Vk.GetApi();

        if (window.VkSurface == null)
            throw new Exception("Failed to initialize Vulkan.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateInstance
    (   byte* applicationName
    ,   byte* engineName
    ,   int instanceExtensionCount
    ,   byte** instanceExtensionNames
#if DEBUG
    ,   byte** layerNames
#endif
    ) {
        // Check if all instance layers are present.
        uint instanceLayerCount = 0;
        _vk!.EnumerateInstanceLayerProperties(&instanceLayerCount, null);
        var instanceLayers = new LayerProperties[instanceLayerCount];
        _vk!.EnumerateInstanceLayerProperties(&instanceLayerCount, instanceLayers);

        var availableLayers = instanceLayers.Select(layer => Marshal.PtrToStringAnsi((nint)layer.LayerName));
        if (!s_requiredInstanceLayers.All(availableLayers.Contains))
            throw new Exception("Not all required instance layers are supported.");

        // Create the instance.

        ApplicationInfo applicationInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = applicationName,
            ApplicationVersion = new Version32(0, 0, 1),
            PEngineName = engineName,
            EngineVersion = new Version32(0, 0, 1),
            ApiVersion = Vk.Version13,
        };

#if DEBUG
        DebugUtilsMessengerCreateInfoEXT debugUtilsMessengerCreateInfoEXT = new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity =
                DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt |
                DebugUtilsMessageSeverityFlagsEXT.InfoBitExt |
                DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType =
                DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt |
                DebugUtilsMessageTypeFlagsEXT.DeviceAddressBindingBitExt,
            PfnUserCallback = (PfnDebugUtilsMessengerCallbackEXT)DebugMessageCallback,
        };
#endif

        InstanceCreateInfo instanceCreateInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &applicationInfo,
            EnabledExtensionCount = (uint)instanceExtensionCount,
            PpEnabledExtensionNames = instanceExtensionNames,
#if DEBUG
            EnabledLayerCount = (uint)s_requiredInstanceLayers.Length,
            PpEnabledLayerNames = layerNames,
            PNext = &debugUtilsMessengerCreateInfoEXT,
#else
            EnabledLayerCount = 0,
#endif
        };

        if (_vk!.CreateInstance(&instanceCreateInfo, null, out _instance) != Result.Success)
            throw new Exception("Failed to create instance.");

        // Finish creating debug messenger.
        // TODO: IDK why, but the above "PNext = &debugUtilsMessengerCreateInfoEXT,"
        // is sufficient and required to create the messenger, so this seems useless.
#if DEBUG
        if (!_vk.TryGetInstanceExtension(_instance, out _debugUtils))
            throw new Exception("Failed to get the debug util extension.");

        if (_debugUtils!.CreateDebugUtilsMessenger(_instance, &debugUtilsMessengerCreateInfoEXT, null, out _debugMessenger) != Result.Success)
            throw new Exception("Failed to create debug messenger.");
#endif
    }

#if DEBUG
    private unsafe uint DebugMessageCallback(
        DebugUtilsMessageSeverityFlagsEXT messageSeverity,
        DebugUtilsMessageTypeFlagsEXT messageTypes,
        DebugUtilsMessengerCallbackDataEXT* pCallbackData,
        void* pUserData
    ) {
        string severityName = messageSeverity switch
        {
            DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt => "Verbose",
            DebugUtilsMessageSeverityFlagsEXT.InfoBitExt    => "Info",
            DebugUtilsMessageSeverityFlagsEXT.WarningBitExt => "Warning",
            DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt   => "Error",
            _                                               => "Unknown",
        };
        string typeName = messageTypes switch
        {
            DebugUtilsMessageTypeFlagsEXT.GeneralBitExt              => "General",
            DebugUtilsMessageTypeFlagsEXT.ValidationBitExt           => "Validation",
            DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt          => "Performance",
            DebugUtilsMessageTypeFlagsEXT.DeviceAddressBindingBitExt => "DeviceAddressBinding",
            _                                                        => "Unknown",
        };
        Console.WriteLine($"[{severityName}|{typeName}]: {Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage)}");
        return Vk.False;
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateSurface(IWindow window)
    {
        if (!_vk!.TryGetInstanceExtension(_instance, out _khrSurface))
            throw new Exception("Failed to get the surface extension.");

        _surface = window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SelectPhysicalDevice()
    {
        var physicalDevices = _vk!.GetPhysicalDevices(_instance);

        foreach (var physicalDevice in physicalDevices)
        {
            if (IsPhysicalDeviceSuitable(physicalDevice))
            {
                _physicalDevice = physicalDevice;
                break;
            }
        }

        if (_physicalDevice.Handle == 0)
            throw new Exception("No suitable physical device found.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe bool IsPhysicalDeviceSuitable(PhysicalDevice physicalDevice)
    {
        // Check if the device has the required extensions.

        uint extensionCount = 0;
        _vk!.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, null);
        var extensions = new ExtensionProperties[extensionCount];
        _vk!.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, extensions);

        var extensionNames = extensions.Select(extension => Marshal.PtrToStringAnsi((nint)extension.ExtensionName));
        if (!s_requiredDeviceExtensions.All(extensionNames.Contains))
            return false;

        // Check if the swap chain is suitable.

        SwapChainSupport swapChainSupport = QuerySwapchainSupport(physicalDevice);

        if (swapChainSupport.Formats.Length == 0 || swapChainSupport.PresentModes.Length == 0)
            return false;

        // Check if the device has the required queue families.

        QueueFamilySupport queueFamilySupport = new();

        uint queueFamilyCount = 0;
        _vk!.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);
        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        _vk!.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamilies);

        for (uint i = 0; i < queueFamilyCount && !queueFamilySupport.IsComplete; ++i)
        {
            var queueFamily = queueFamilies[i];

            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                queueFamilySupport.GraphicsFamily = i;

            _khrSurface!.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, _surface, out var presentationSupport);
            if (presentationSupport)
                queueFamilySupport.PresentFamily = i;
        }

        if (!queueFamilySupport.IsComplete)
            return false;

        // Return everything.

        _queueFamilySupport = queueFamilySupport;
        _swapchainSupport = swapChainSupport;
        return true;
    }

    private unsafe SwapChainSupport QuerySwapchainSupport(PhysicalDevice physicalDevice)
    {
        SwapChainSupport swapChainSupport = new();

        _khrSurface!.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, _surface, out swapChainSupport.Capabilities);

        uint formatCount = 0;
        _khrSurface!.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, &formatCount, null);
        var formats = new SurfaceFormatKHR[formatCount];
        _khrSurface!.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, &formatCount, formats);
        swapChainSupport.Formats = formats;

        uint presentModeCount = 0;
        _khrSurface!.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, &presentModeCount, null);
        var presentModes = new PresentModeKHR[presentModeCount];
        _khrSurface!.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, &presentModeCount, presentModes);
        swapChainSupport.PresentModes = presentModes;

        return swapChainSupport;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateDevice
    (   byte** deviceExtensionNames
#if DEBUG
    ,   byte** layerNames
#endif
    ) {
        uint graphicsFamily = _queueFamilySupport.GraphicsFamily!.Value;
        uint presentationFamily = _queueFamilySupport.PresentFamily!.Value;
        var uniqueQueueFamilies = new[] { graphicsFamily, presentationFamily }.Distinct().ToArray();

        float priority = 1f;
        var deviceQueueCreateInfos = new DeviceQueueCreateInfo[uniqueQueueFamilies.Length];
        for (uint i = 0; i < uniqueQueueFamilies.Length; ++i)
        {
            deviceQueueCreateInfos[i] = new()
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueQueueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &priority,
            };
        }

        PhysicalDeviceFeatures physicalDeviceFeatures = new();

        fixed (DeviceQueueCreateInfo* deviceQueueCreateInfosPtr = deviceQueueCreateInfos)
        {
            DeviceCreateInfo deviceCreateInfo = new()
            {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)deviceQueueCreateInfos.Length,
                PQueueCreateInfos = deviceQueueCreateInfosPtr,
                EnabledExtensionCount = (uint)s_requiredDeviceExtensions.Length,
                PpEnabledExtensionNames = deviceExtensionNames,
                PEnabledFeatures = &physicalDeviceFeatures,
#if DEBUG
                EnabledLayerCount = (uint)s_requiredInstanceLayers.Length,
                PpEnabledLayerNames = layerNames,
#endif
            };

            if (_vk!.CreateDevice(_physicalDevice, &deviceCreateInfo, null, out _device) != Result.Success)
                throw new Exception("Failed to create device");
        }

        _vk!.GetDeviceQueue(_device, graphicsFamily, 0, out _graphicsQueue);
        _vk!.GetDeviceQueue(_device, presentationFamily, 0, out _presentationQueue);
    }

    // Explicitly NOT aggressively inlined because the swap chain
    // will need to be recreated whenever the window is resized.
    private unsafe void CreateSwapchain()
    {
        _swapchainPresentMode = SelectPresentMode();
        _swapchainImageExtent = SelectExtent();
        uint minImageCount = SelectImageCount();

        SwapchainCreateInfoKHR swapchainCreateInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = minImageCount,
            ImageFormat = _swapchainImageFormat.Format,
            ImageColorSpace = _swapchainImageFormat.ColorSpace,
            ImageExtent = _swapchainImageExtent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive,

            PreTransform = _swapchainSupport.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = _swapchainPresentMode,
            Clipped = true,
            OldSwapchain = _swapchain,
        };

        uint graphicsFamily = _queueFamilySupport.GraphicsFamily!.Value;
        uint presentationFamily = _queueFamilySupport.PresentFamily!.Value;
        var queueFamilyIndices = stackalloc[] { graphicsFamily, presentationFamily };
        if (graphicsFamily != presentationFamily)
        {
            swapchainCreateInfo.ImageSharingMode = SharingMode.Concurrent;
            swapchainCreateInfo.QueueFamilyIndexCount = 2;
            swapchainCreateInfo.PQueueFamilyIndices = queueFamilyIndices;
        }

        if (_khrSwapchain == null && !_vk!.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
            throw new Exception("Failed to get the swap chain extension.");

        // When recreating the swapchain, we must wait for the device to be available.
        if (_swapchain.Handle != 0)
            _vk!.DeviceWaitIdle(_device);

        SwapchainKHR swapChain;
        var result = _khrSwapchain!.CreateSwapchain(_device, &swapchainCreateInfo, null, &swapChain);
        if (result != Result.Success)
            throw new Exception("Failed to create swapchain.");

        DisposeSwapchain();
        _swapchain = swapChain;

        uint actualImageCount;
        _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &actualImageCount, null);
        var images = new Image[actualImageCount];
        _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &actualImageCount, images);
        _swapchainImages = images;
        _swapchainImageCount = actualImageCount;

        CreateSwapChainImageViews();
        CreateSwapChainFramebuffers();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RecreateSwapchain()
    {
        _swapchainSupport = QuerySwapchainSupport(_physicalDevice);
        CreateSwapchain();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SurfaceFormatKHR SelectSurfaceFormat()
    {
        ref var surfaceFormats = ref _swapchainSupport.Formats;
        foreach (var surfaceFormat in surfaceFormats)
            if (surfaceFormat.Format == Format.R8G8B8A8Unorm)
                return surfaceFormat;
        return surfaceFormats[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PresentModeKHR SelectPresentMode()
    {
        ref var presentModes = ref _swapchainSupport.PresentModes;
        foreach (var presentMode in presentModes)
            if (presentMode == PresentModeKHR.MailboxKhr)
                return presentMode;
        return PresentModeKHR.FifoKhr; // Guaranteed to be available.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Extent2D SelectExtent()
    {
        return _swapchainSupport.Capabilities.CurrentExtent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint SelectImageCount()
    {
        ref var surfaceCapabilities = ref _swapchainSupport.Capabilities;
        uint imageCount = surfaceCapabilities.MinImageCount + 1;
        if (surfaceCapabilities.MaxImageCount > 0 && imageCount > surfaceCapabilities.MaxImageCount)
            imageCount = surfaceCapabilities.MaxImageCount;
        return imageCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateSwapChainImageViews()
    {
        _swapchainImageViews = new ImageView[_swapchainImages!.Length];

        ImageViewCreateInfo imageViewCreateInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            ViewType = ImageViewType.Type2D,
            Format = _swapchainImageFormat.Format,
            Components =
            {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity,
            },
            SubresourceRange =
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        for (uint i = 0; i < _swapchainImages.Length; ++i)
        {
            imageViewCreateInfo.Image = _swapchainImages[i];
            if (_vk!.CreateImageView(_device, &imageViewCreateInfo, null, out _swapchainImageViews[i]) != Result.Success)
                throw new Exception("Failed to create image view.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CreateRenderPass()
    {
        AttachmentDescription[] renderPassDescription =
        [
            new()
            {
                Format = _swapchainImageFormat.Format,
                Samples = SampleCountFlags.Count1Bit,
                LoadOp = AttachmentLoadOp.Clear, // TODO: eventually replace with AttachmentLoadOp.DontCare
                StoreOp = AttachmentStoreOp.Store,
                StencilLoadOp = AttachmentLoadOp.DontCare,
                StencilStoreOp = AttachmentStoreOp.DontCare,
                InitialLayout = ImageLayout.Undefined,
                FinalLayout = ImageLayout.PresentSrcKhr,
            },
        ];
        AttachmentReference[] renderPassColorAttachments =
        [
            new()
            {
                Attachment = 0,
                Layout = ImageLayout.ColorAttachmentOptimal,
            },
        ];
        SubpassDependency[] subpassDependencies =
        [
            new()
            {
                SrcSubpass = Vk.SubpassExternal,
                DstSubpass = 0,
                SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                SrcAccessMask = AccessFlags.None,
                DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
            },
        ];
        _renderPass = new(renderPassDescription, renderPassColorAttachments, subpassDependencies);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateSwapChainFramebuffers()
    {
        _swapchainFramebuffers = new Silk.NET.Vulkan.Framebuffer[_swapchainImageViews!.Length];

        for (int i = 0; i < _swapchainImageViews.Length; ++i)
        {
            var attachments = stackalloc[] { _swapchainImageViews[i] };

            //ImageView
            FramebufferCreateInfo framebufferCreateInfo = new()
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass!.Handle,
                AttachmentCount = 1, // TODO
                PAttachments = attachments, // TODO
                Width = _swapchainImageExtent.Width,
                Height = _swapchainImageExtent.Height,
                Layers = 1, // TODO
            };

            if (_vk!.CreateFramebuffer(_device, &framebufferCreateInfo, null, out _swapchainFramebuffers[i]) != Result.Success)
                throw new Exception("Failed to create swap chain framebuffer.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateCommandPool()
    {
        uint graphicsFamily = _queueFamilySupport.GraphicsFamily!.Value;

        CommandPoolCreateInfo commandPoolCreateInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = graphicsFamily,
        };

        if (_vk!.CreateCommandPool(_device, &commandPoolCreateInfo, null, out _commandPool) != Result.Success)
            throw new Exception("Failed to create command pool.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateCommandBuffers()
    {
        _commandBuffers = new CommandBuffer[FramesInFlight];

        CommandBufferAllocateInfo commandBufferAllocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary, // TODO
            CommandBufferCount = FramesInFlight,
        };

        fixed (CommandBuffer* commandBuffersPtr = _commandBuffers)
        {
            if (_vk!.AllocateCommandBuffers(_device, &commandBufferAllocateInfo, commandBuffersPtr) != Result.Success)
                throw new Exception("Failed to allocate command buffers.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateSynchronizationObjects()
    {
        _imageAvailableSemaphores = new Silk.NET.Vulkan.Semaphore[FramesInFlight];
        _frameFinishedSemaphores = new Silk.NET.Vulkan.Semaphore[FramesInFlight];
        _frameInFlightFences = new Fence[FramesInFlight];

        SemaphoreCreateInfo semaphoreCreateInfo = new()
        {
            SType = StructureType.SemaphoreCreateInfo,
        };

        FenceCreateInfo fenceCreateInfo = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };

        for (uint i = 0; i < FramesInFlight; ++i)
        {
            if (_vk!.CreateSemaphore(_device, &semaphoreCreateInfo, null, out _imageAvailableSemaphores[i]) != Result.Success)
                throw new Exception("Failed to create available image semaphore.");
            if (_vk!.CreateSemaphore(_device, &semaphoreCreateInfo, null, out _frameFinishedSemaphores[i]) != Result.Success)
                throw new Exception("Failed to create finished frame semaphore.");
            if (_vk!.CreateFence(_device, &fenceCreateInfo, null, out _frameInFlightFences[i]) != Result.Success)
                throw new Exception("Failed to create frame in flight fence.");
        }
    }

    internal unsafe bool BeginFrame()
    {
        var commandBuffer = _commandBuffers![_currentFrameInFlightIndex];
        var imageAvailableSemaphore = _imageAvailableSemaphores![_currentFrameInFlightIndex];
        var frameInFlightFence = _frameInFlightFences![_currentFrameInFlightIndex];

        _vk!.WaitForFences(_device, 1, &frameInFlightFence, true, ulong.MaxValue);
        var result = _khrSwapchain!.AcquireNextImage(_device, _swapchain, ulong.MaxValue, imageAvailableSemaphore, default, ref _swapchainImageIndex);

        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr)
        {
            RecreateSwapchain();
            return false;
        }

        _vk!.ResetFences(_device, 1, &frameInFlightFence);

        _vk!.ResetCommandBuffer(commandBuffer, CommandBufferResetFlags.None);

        CommandBufferBeginInfo commandBufferBeginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.None, // TODO
        };

        if (_vk!.BeginCommandBuffer(commandBuffer, &commandBufferBeginInfo) != Result.Success)
            throw new Exception("Failed to begin command buffer.");

        return true;
    }

    internal unsafe void EndFrame()
    {
        var commandBuffer = _commandBuffers![_currentFrameInFlightIndex];
        var imageAvailableSemaphore = _imageAvailableSemaphores![_currentFrameInFlightIndex];
        var frameFinishedSemaphore = _frameFinishedSemaphores![_currentFrameInFlightIndex];
        var frameInFlightFence = _frameInFlightFences![_currentFrameInFlightIndex];

        if (_vk!.EndCommandBuffer(commandBuffer) != Result.Success)
            throw new Exception("Failed to end command buffer.");

        var pipelineStageFlags = PipelineStageFlags.ColorAttachmentOutputBit;
        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &imageAvailableSemaphore,
            PWaitDstStageMask = &pipelineStageFlags,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &frameFinishedSemaphore,
        };

        if (_vk!.QueueSubmit(_graphicsQueue, 1, &submitInfo, frameInFlightFence) != Result.Success)
            throw new Exception("Failed to submit command buffer.");

        var swapChain = _swapchain;
        var swapChainImageIndex = _swapchainImageIndex;
        PresentInfoKHR presentInfoKHR = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &frameFinishedSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapChain,
            PImageIndices = &swapChainImageIndex,
        };

        switch (_khrSwapchain!.QueuePresent(_presentationQueue, &presentInfoKHR))
        {
            case Result.ErrorOutOfDateKhr:
            case Result.SuboptimalKhr:
                RecreateSwapchain();
                break;
            case Result.Success:
                break;
            default:
                throw new Exception("Failed to present the frame to the presentation the queue.");
        }

        _currentFrameInFlightIndex = (_currentFrameInFlightIndex + 1) % FramesInFlight;
    }

    internal void WaitForDevice()
    {
        _vk!.DeviceWaitIdle(_device);
    }

    // TODO
    public unsafe void Draw()
    {
        var commandBuffer = _commandBuffers![_currentFrameInFlightIndex];

        BeginRenderPass();
        _vk!.CmdDraw(commandBuffer, 3, 1, 0, 0);
        EndRenderPass();
    }

    // TODO
    private unsafe void BeginRenderPass()
    {
        var commandBuffer = _commandBuffers![_currentFrameInFlightIndex];

        var clearValue = stackalloc[] { new ClearValue(new ClearColorValue(0.3f, 0.5f, 1.0f, 1.0f)) };
        RenderPassBeginInfo renderPassBeginInfo = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass!.Handle,
            Framebuffer = _swapchainFramebuffers![_swapchainImageIndex], // TODO
            RenderArea = { Offset = { X = 0, Y = 0 }, Extent = _swapchainImageExtent },
            ClearValueCount = 1, // TODO: eventually replace with 0 when render pass' LoadOp is AttachmentLoadOp.DontCare
            PClearValues = clearValue,
        };

        _vk!.CmdBeginRenderPass(commandBuffer, &renderPassBeginInfo, SubpassContents.Inline);
    }

    // TODO
    private void EndRenderPass()
    {
        var commandBuffer = _commandBuffers![_currentFrameInFlightIndex];

        _vk!.CmdEndRenderPass(commandBuffer);
    }
}

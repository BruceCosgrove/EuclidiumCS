using Silk.NET.Core.Native;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using System.Runtime.CompilerServices;

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
    private Image[]? _images;
    private Format _surfaceFormat;
    private Extent2D _extent;

    public Vk VK => _vk!;
    public Instance Instance => _instance!;

    public static unsafe GraphicsContext? Create(IWindow window)
    {
        GraphicsContext context = new(window);
        return context._vk != null ? context : null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe GraphicsContext(IWindow window)
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
        var applicationName = (byte*)Marshal.StringToHGlobalAnsi("EuclidiumCS");
        var engineName = (byte*)Marshal.StringToHGlobalAnsi("Euclidium");
        var instanceExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(instanceExtensions);
        var deviceExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(s_requiredDeviceExtensions);
#if DEBUG
        var layerNames = (byte**)SilkMarshal.StringArrayToPtr(s_requiredInstanceLayers);
#endif

        try
        {
            // Create the API.
            CreateAPI(window);

            // Create the instance.
            CreateInstance(applicationName, engineName, instanceExtensions.Length, instanceExtensionNames
#if DEBUG
                , layerNames
#endif
            );

            // Create the surface.
            CreateSurface(window);

            // Pick the physical device and get relevant supported components.
            QueueFamilySupport queueFamilySupport = new();
            SwapChainSupport swapChainSupport = new();
            SelectPhysicalDevice(ref queueFamilySupport, ref swapChainSupport);

            // Create logical device and get the selected queues.
            CreateDevice(ref queueFamilySupport, deviceExtensionNames
#if DEBUG
                , layerNames
#endif
            );

            // Create swap chain.
            CreateSwapChain(ref queueFamilySupport, ref swapChainSupport);
        }
        catch (Exception e)
        {
            Dispose(); // Dispose what was partially created.
            Console.Error.WriteLine(e);
        }
        finally
        {
            // Deallocate all the unmanaged objects, regardless of exceptions.
            Marshal.FreeHGlobal((nint)applicationName);
            Marshal.FreeHGlobal((nint)engineName);
            SilkMarshal.Free((nint)instanceExtensionNames);
            SilkMarshal.Free((nint)deviceExtensionNames);
#if DEBUG
            SilkMarshal.Free((nint)layerNames);
#endif
        }
    }

    // Disposes any created unmanaged resources in the reverse order they were created in.
    // This even works if it was only partially created or already partially or entirely disposed.
    public unsafe void Dispose()
    {
        Dispose(ref _swapchain, () => _khrSwapchain!.DestroySwapchain(_device, _swapchain, null));
        Dispose(ref _khrSwapchain);
        Dispose(ref _device, () => _vk!.DestroyDevice(_device, null));
        Dispose(ref _surface, () => _khrSurface!.DestroySurface(_instance, _surface, null));
        Dispose(ref _khrSurface);
#if DEBUG
        Dispose(ref _debugMessenger, () => _debugUtils!.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null));
        Dispose(ref _debugUtils);
#endif
        Dispose(ref _instance, () => _vk!.DestroyInstance(_instance, null));
        Dispose(ref _vk);
    }

    private static void Dispose<T>(ref T handle, Action destroyFunc) where T : struct
    {
        if (!handle.Equals(default(T)))
        {
            destroyFunc();
            handle = default;
        }
    }

    private static void Dispose<T>(ref T? disposable) where T : class, IDisposable
    {
        if (disposable != null)
        {
            disposable.Dispose();
            disposable = null;
        }
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
    private unsafe bool IsPhysicalDeviceSuitable(
        PhysicalDevice physicalDevice,
        ref QueueFamilySupport outQueueFamilySupport,
        ref SwapChainSupport outSwapChainSupport
    ) {
        // Check if the device has the required extensions.

        uint extensionCount = 0;
        _vk!.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, null);
        var extensions = new ExtensionProperties[extensionCount];
        _vk!.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, extensions);

        var extensionNames = extensions.Select(extension => Marshal.PtrToStringAnsi((nint)extension.ExtensionName));
        if (!s_requiredDeviceExtensions.All(extensionNames.Contains))
            return false;

        // Check if the swap chain is suitable.

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

        outQueueFamilySupport = queueFamilySupport;
        outSwapChainSupport = swapChainSupport;
        return true;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateSurface(IWindow window)
    {
        if (!_vk!.TryGetInstanceExtension(_instance, out _khrSurface))
            throw new Exception("Failed to get the surface extension.");

        _surface = window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SelectPhysicalDevice(
        ref QueueFamilySupport queueFamilySupport,
        ref SwapChainSupport swapChainSupport
    ) {
        var physicalDevices = _vk!.GetPhysicalDevices(_instance);

        foreach (var physicalDevice in physicalDevices)
        {
            if (IsPhysicalDeviceSuitable(physicalDevice, ref queueFamilySupport, ref swapChainSupport))
            {
                _physicalDevice = physicalDevice;
                break;
            }
        }

        if (_physicalDevice.Handle == 0)
            throw new Exception("No suitable physical device found.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreateDevice
    (   ref QueueFamilySupport queueFamilySupport
    ,   byte** deviceExtensionNames
#if DEBUG
    ,   byte** layerNames
#endif
    ) {
        uint graphicsFamily = queueFamilySupport.GraphicsFamily!.Value;
        uint presentationFamily = queueFamilySupport.PresentFamily!.Value;
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
    private unsafe void CreateSwapChain(
        ref QueueFamilySupport queueFamilySupport,
        ref SwapChainSupport swapChainSupport,
        SwapchainKHR oldSwapchain = new()
    ) {
        var extent = SelectExtent(ref swapChainSupport.Capabilities);
        var surfaceFormat = SelectSurfaceFormat(swapChainSupport.Formats);
        var presentMode = SelectPresentMode(swapChainSupport.PresentModes);
        var imageCount = SelectImageCount(ref swapChainSupport.Capabilities);

        SwapchainCreateInfoKHR swapchainCreateInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ColorAttachmentBit,
            ImageSharingMode = SharingMode.Exclusive,

            PreTransform = swapChainSupport.Capabilities.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = oldSwapchain
        };

        uint graphicsFamily = queueFamilySupport.GraphicsFamily!.Value;
        uint presentationFamily = queueFamilySupport.PresentFamily!.Value;
        var queueFamilyIndices = stackalloc[] { graphicsFamily, presentationFamily };
        if (graphicsFamily != presentationFamily)
        {
            swapchainCreateInfo.ImageSharingMode = SharingMode.Concurrent;
            swapchainCreateInfo.QueueFamilyIndexCount = 2;
            swapchainCreateInfo.PQueueFamilyIndices = queueFamilyIndices;
        }

        if (!_vk!.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
            throw new Exception("Failed to get the swap chain extension.");

        var result = _khrSwapchain!.CreateSwapchain(_device, &swapchainCreateInfo, null, out _swapchain);
        if (result != Result.Success)
            throw new Exception("Failed to create swapchain.");

        _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &imageCount, null);
        var images = new Image[imageCount];
        _khrSwapchain!.GetSwapchainImages(_device, _swapchain, &imageCount, images);
        _images = images;

        _surfaceFormat = surfaceFormat.Format;
        _extent = extent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SurfaceFormatKHR SelectSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> surfaceFormats)
    {
        foreach (var surfaceFormat in surfaceFormats)
            if (surfaceFormat.Format == Format.B8G8R8A8Srgb && surfaceFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return surfaceFormat;
        return surfaceFormats[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private PresentModeKHR SelectPresentMode(IReadOnlyList<PresentModeKHR> presentModes)
    {
        foreach (var presentMode in presentModes)
            if (presentMode == PresentModeKHR.MailboxKhr)
                return presentMode;
        return PresentModeKHR.FifoKhr; // Guaranteed to be available.
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Extent2D SelectExtent(ref SurfaceCapabilitiesKHR surfaceCapabilities)
    {
        return surfaceCapabilities.CurrentExtent;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint SelectImageCount(ref SurfaceCapabilitiesKHR surfaceCapabilities)
    {
        uint imageCount = surfaceCapabilities.MinImageCount + 1;
        if (surfaceCapabilities.MaxImageCount > 0 && imageCount > surfaceCapabilities.MaxImageCount)
            imageCount = surfaceCapabilities.MaxImageCount;
        return imageCount;
    }
}

using Silk.NET.Core.Native;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

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

    private Vk _vk;
    private Instance _instance;
    private KhrSurface _khrSurface;
    private SurfaceKHR _surface;
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _presentationQueue;
    private KhrSwapchain _khrSwapchain;
    private SwapchainKHR _swapchain;
    private Image[] _images;
    private Format _surfaceFormat;
    private Extent2D _extent;

#if DEBUG
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;

    private static readonly string[] s_requiredValidationLayers =
    [
        "VK_LAYER_KHRONOS_validation",
    ];
#endif

    private static readonly string[] s_requiredDeviceExtensions =
    [
        KhrSwapchain.ExtensionName,
    ];

    public Vk VK => _vk;
    public Instance Instance => _instance!;

    public unsafe GraphicsContext(IWindow window)
    {
        _vk = Vk.GetApi();

        if (window.VkSurface == null)
        {
            Console.Error.WriteLine("Failed to initialize Vulkan window.");
            Environment.Exit(1);
        }

#if DEBUG
        if (!AreRequiredValidationLayersSupported())
        {
            Console.Error.WriteLine("Not all required validation layers are supported.");
            Environment.Exit(1);
        }
#endif

        // Create the instance.

        var applicationName = (byte*)Marshal.StringToHGlobalAnsi("EuclidiumCS");
        var engineName = (byte*)Marshal.StringToHGlobalAnsi("Euclidium");

        ApplicationInfo applicationInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = applicationName,
            ApplicationVersion = new Version32(0, 0, 1),
            PEngineName = engineName,
            EngineVersion = new Version32(0, 0, 1),
            ApiVersion = Vk.Version13,
        };

        var surfaceExtensions = window.VkSurface!.GetRequiredExtensions(out uint surfaceExtensionCount);
        var instanceExtensions = SilkMarshal.PtrToStringArray((nint)surfaceExtensions, (int)surfaceExtensionCount);
#if DEBUG
        instanceExtensions = [..instanceExtensions, ExtDebugUtils.ExtensionName];
#endif

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

        var instanceExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(instanceExtensions);
#if DEBUG
        var layerNames = (byte**)SilkMarshal.StringArrayToPtr(s_requiredValidationLayers);
#endif

        InstanceCreateInfo instanceCreateInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &applicationInfo,
            EnabledExtensionCount = (uint)instanceExtensions.Length,
            PpEnabledExtensionNames = instanceExtensionNames,
#if DEBUG
            EnabledLayerCount = (uint)s_requiredValidationLayers.Length,
            PpEnabledLayerNames = layerNames,
            PNext = &debugUtilsMessengerCreateInfoEXT,
#else
            EnabledLayerCount = 0,
#endif
        };

        var result = _vk.CreateInstance(&instanceCreateInfo, null, out _instance);
        if (result != Result.Success)
        {
            Console.Error.WriteLine("Failed to create instance.");
            Environment.Exit(1);
        }

        // Finish creating debug messenger.
        // TODO: IDK why, but the above "PNext = &debugUtilsMessengerCreateInfoEXT,"
        // is sufficient and required to create the messenger, so this seems useless.
#if DEBUG
        if (!_vk.TryGetInstanceExtension(_instance, out _debugUtils))
        {
            Console.Error.WriteLine("Failed to get the debug util extension.");
            Environment.Exit(1);
        }

        result = _debugUtils!.CreateDebugUtilsMessenger(_instance, &debugUtilsMessengerCreateInfoEXT, null, out _debugMessenger);
        if (result != Result.Success)
        {
            Console.Error.WriteLine("Failed to create debug messenger.");
            Environment.Exit(1);
        }
#endif

        // Create the surface.

        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
        {
            Console.Error.WriteLine("Failed to get the surface extension.");
            Environment.Exit(1);
        }

        _surface = window.VkSurface.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();

        // Pick the physical device.

        var physicalDevices = _vk.GetPhysicalDevices(_instance);
        QueueFamilySupport queueFamilySupport = new();
        SwapChainSupport swapChainSupport = new();
        foreach (var physicalDevice in physicalDevices)
        {
            if (IsPhysicalDeviceSuitable(physicalDevice, ref queueFamilySupport, ref swapChainSupport))
            {
                _physicalDevice = physicalDevice;
                break;
            }
        }

        if (_physicalDevice.Handle == 0)
        {
            Console.Error.WriteLine("No suitable physical device found.");
            Environment.Exit(1);
        }

        // Create logical device and get the selected queues.
        
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

        var deviceExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(s_requiredDeviceExtensions);

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
                EnabledLayerCount = (uint)s_requiredValidationLayers.Length,
                PpEnabledLayerNames = layerNames,
#endif
            };

            result = _vk.CreateDevice(_physicalDevice, &deviceCreateInfo, null, out _device);
        }

        if (result != Result.Success)
        {
            Console.Error.WriteLine("Failed to create device");
            Environment.Exit(1);
        }

        _vk.GetDeviceQueue(_device, graphicsFamily, 0, out _graphicsQueue);
        _vk.GetDeviceQueue(_device, presentationFamily, 0, out _presentationQueue);

        // Create swap chain.

        CreateSwapChain(ref queueFamilySupport, ref swapChainSupport);

        // TODO: Make sure these are always deleted before returning.
        // Right now, "Environment.Exit(1);" takes care of the memory leaks.
        Marshal.FreeHGlobal((nint)applicationName);
        Marshal.FreeHGlobal((nint)engineName);
        SilkMarshal.Free((nint)instanceExtensionNames);
        SilkMarshal.Free((nint)deviceExtensionNames);
#if DEBUG
        SilkMarshal.Free((nint)layerNames);
#endif
    }

    public unsafe void Dispose()
    {
        _khrSwapchain.DestroySwapchain(_device, _swapchain, null);
        _khrSwapchain.Dispose();
        _vk.DestroyDevice(_device, null);
#if DEBUG
        _debugUtils!.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
#endif
        _khrSurface.DestroySurface(_instance, _surface, null);
        _khrSurface.Dispose();
        _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
    }

#if DEBUG
    private unsafe bool AreRequiredValidationLayersSupported()
    {
        uint layerCount = 0;
        _vk.EnumerateInstanceLayerProperties(&layerCount, null);
        var layers = new LayerProperties[layerCount];
        _vk.EnumerateInstanceLayerProperties(&layerCount, layers);
        var availableLayers = layers.Select(layer => Marshal.PtrToStringAnsi((nint)layer.LayerName));
        return s_requiredValidationLayers.All(availableLayers.Contains);
    }

    private unsafe uint DebugMessageCallback(DebugUtilsMessageSeverityFlagsEXT messageSeverity, DebugUtilsMessageTypeFlagsEXT messageTypes, DebugUtilsMessengerCallbackDataEXT* pCallbackData, void* pUserData)
    {
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

    private unsafe bool IsPhysicalDeviceSuitable(PhysicalDevice physicalDevice, ref QueueFamilySupport outQueueFamilySupport, ref SwapChainSupport outSwapChainSupport)
    {
        // Check if the device has the required extensions.

        uint extensionCount = 0;
        _vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, null);
        var extensions = new ExtensionProperties[extensionCount];
        _vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, extensions);

        var extensionNames = extensions.Select(extension => Marshal.PtrToStringAnsi((nint)extension.ExtensionName));
        if (!s_requiredDeviceExtensions.All(extensionNames.Contains))
            return false;

        // Check if the swap chain is suitable.

        SwapChainSupport swapChainSupport = new();

        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(physicalDevice, _surface, out swapChainSupport.Capabilities);

        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, &formatCount, null);
        var formats = new SurfaceFormatKHR[formatCount];
        _khrSurface.GetPhysicalDeviceSurfaceFormats(physicalDevice, _surface, &formatCount, formats);
        swapChainSupport.Formats = formats;

        uint presentModeCount = 0;
        _khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, &presentModeCount, null);
        var presentModes = new PresentModeKHR[presentModeCount];
        _khrSurface.GetPhysicalDeviceSurfacePresentModes(physicalDevice, _surface, &presentModeCount, presentModes);
        swapChainSupport.PresentModes = presentModes;

        if (swapChainSupport.Formats.Length == 0 || swapChainSupport.PresentModes.Length == 0)
            return false;

        // Check if the device has the required queue families.

        QueueFamilySupport queueFamilySupport = new();

        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);
        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        _vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamilies);

        for (uint i = 0; i < queueFamilyCount && !queueFamilySupport.IsComplete; ++i)
        {
            var queueFamily = queueFamilies[i];

            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                queueFamilySupport.GraphicsFamily = i;

            _khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, _surface, out var presentationSupport);
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

    private unsafe void CreateSwapChain(ref QueueFamilySupport queueFamilySupport, ref SwapChainSupport swapChainSupport, SwapchainKHR oldSwapchain = new())
    {
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

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
        {
            Console.Error.WriteLine("Failed to get the swap chain extension.");
            Environment.Exit(1);
        }

        var result = _khrSwapchain.CreateSwapchain(_device, &swapchainCreateInfo, null, out _swapchain);
        if (result != Result.Success)
        {
            Console.Error.WriteLine("Failed to create swapchain.");
            Environment.Exit(1);
        }

        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, null);
        var images = new Image[imageCount];
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, images);
        _images = images;

        _surfaceFormat = surfaceFormat.Format;
        _extent = extent;
    }

    private SurfaceFormatKHR SelectSurfaceFormat(IReadOnlyList<SurfaceFormatKHR> surfaceFormats)
    {
        foreach (var surfaceFormat in surfaceFormats)
            if (surfaceFormat.Format == Format.B8G8R8A8Srgb && surfaceFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return surfaceFormat;
        return surfaceFormats[0];
    }

    private PresentModeKHR SelectPresentMode(IReadOnlyList<PresentModeKHR> presentModes)
    {
        foreach (var presentMode in presentModes)
            if (presentMode == PresentModeKHR.MailboxKhr)
                return presentMode;
        return PresentModeKHR.FifoKhr; // Guaranteed to be available.
    }
    private Extent2D SelectExtent(ref readonly SurfaceCapabilitiesKHR surfaceCapabilities)
    {
        return surfaceCapabilities.CurrentExtent;
    }

    private uint SelectImageCount(ref readonly SurfaceCapabilitiesKHR surfaceCapabilities)
    {
        uint imageCount = surfaceCapabilities.MinImageCount + 1;
        if (surfaceCapabilities.MaxImageCount > 0 && imageCount > surfaceCapabilities.MaxImageCount)
            imageCount = surfaceCapabilities.MaxImageCount;
        return imageCount;
    }
}

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
    private struct QueueFamilyIndices
    {
        public uint? GraphicsFamily;
        public uint? PresentationFamily;

        public readonly bool IsComplete => GraphicsFamily.HasValue && PresentationFamily.HasValue;
    }

    private Vk _vk;
    private Instance _instance;
    private KhrSurface _khrSurface; // why
    private SurfaceKHR _surfaceKHR; // tho
    private PhysicalDevice _physicalDevice;
    private Device _device;
    private Queue _graphicsQueue;
    private Queue _presentationQueue;

#if DEBUG
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;

    private static readonly string[] s_validationLayers =
    [
        "VK_LAYER_KHRONOS_validation",
    ];
#endif

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
        var extensions = SilkMarshal.PtrToStringArray((nint)surfaceExtensions, (int)surfaceExtensionCount);
#if DEBUG
        extensions = [..extensions, ExtDebugUtils.ExtensionName];
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

        var extensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions);
#if DEBUG
        var layerNames = (byte**)SilkMarshal.StringArrayToPtr(s_validationLayers);
#endif

        InstanceCreateInfo instanceCreateInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &applicationInfo,
            EnabledExtensionCount = (uint)extensions.Length,
            PpEnabledExtensionNames = extensionNames,
#if DEBUG
            EnabledLayerCount = (uint)s_validationLayers.Length,
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
        if (_vk.TryGetInstanceExtension(_instance, out _debugUtils))
        {
            result = _debugUtils!.CreateDebugUtilsMessenger(_instance, &debugUtilsMessengerCreateInfoEXT, null, out _debugMessenger);

            if (result != Result.Success)
            {
                Console.Error.WriteLine("Failed to create debug messenger.");
                Environment.Exit(1);
            }
        }
#endif

        // Create the surface.
        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
        {
            Console.Error.WriteLine("Failed to create surface.");
            Environment.Exit(1);
        }

        _surfaceKHR = window.VkSurface.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();

        // Pick the physical device.

        var physicalDevices = _vk.GetPhysicalDevices(_instance);
        QueueFamilyIndices? queueFamilyIndices = null;
        foreach (var physicalDevice in physicalDevices)
        {
            if (IsPhysicalDeviceSuitable(physicalDevice, out queueFamilyIndices))
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
        
        uint graphicsFamily = queueFamilyIndices!.Value.GraphicsFamily!.Value;
        uint presentationFamily = queueFamilyIndices!.Value.PresentationFamily!.Value;

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
                EnabledExtensionCount = 0,
                PEnabledFeatures = &physicalDeviceFeatures,
#if DEBUG
                EnabledLayerCount = (uint)s_validationLayers.Length,
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

        // TODO: Make sure these are always deleted before returning.
        // Right now, "Environment.Exit(1);" takes care of the memory leaks.
        Marshal.FreeHGlobal((nint)applicationName);
        Marshal.FreeHGlobal((nint)engineName);
        SilkMarshal.Free((nint)extensionNames);
#if DEBUG
        SilkMarshal.Free((nint)layerNames);
#endif
    }

    public unsafe void Dispose()
    {
        _vk.DestroyDevice(_device, null);
#if DEBUG
        _debugUtils!.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
#endif
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
        var availableLayers = layers.Select(layer => Marshal.PtrToStringAnsi((nint)layer.LayerName)).ToHashSet();
        return s_validationLayers.All(availableLayers.Contains);
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

    private unsafe bool IsPhysicalDeviceSuitable(PhysicalDevice physicalDevice, out QueueFamilyIndices? queueFamilyIndices)
    {
        QueueFamilyIndices indices = new();

        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);
        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        _vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamilies);

        for (uint i = 0; i < queueFamilyCount; ++i)
        {
            var queueFamily = queueFamilies[i];

            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                indices.GraphicsFamily = i;

            _khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, _surfaceKHR, out var presentationSupport);
            if (presentationSupport)
                indices.PresentationFamily = i;

            if (indices.IsComplete)
            {
                queueFamilyIndices = indices;
                return true;
            }
        }

        queueFamilyIndices = null;
        return false;
    }
}

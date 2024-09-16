using Silk.NET.Core.Native;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Euclidium.Rendering;

public sealed class GraphicsContext : IDisposable
{
    private Vk? _vk;
    private IVkSurface? _surface; // Not owned, just a reference.
    private Instance _instance;

#if DEBUG
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;

    private static readonly string[] s_validationLayers =
    [
        "VK_LAYER_KHRONOS_validation",
    ];
#endif

    public Vk VK => _vk!;
    public Instance Instance => _instance!;

    public unsafe GraphicsContext(IWindow window)
    {
        _vk = Vk.GetApi();
        _surface = window.VkSurface;

        if (_surface == null)
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

        ApplicationInfo applicationInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)Marshal.StringToHGlobalAnsi("EuclidiumCS"),
            ApplicationVersion = new Version32(0, 0, 1),
            PEngineName = (byte*)Marshal.StringToHGlobalAnsi("Euclidium"),
            EngineVersion = new Version32(0, 0, 1),
            ApiVersion = Vk.Version13,
        };

        var extensions = GetRequiredExtensions();

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
            EnabledExtensionCount = (uint)extensions.Length,
            PpEnabledExtensionNames = (byte**)SilkMarshal.StringArrayToPtr(extensions),
#if DEBUG
            EnabledLayerCount = (uint)s_validationLayers.Length,
            PpEnabledLayerNames = (byte**)SilkMarshal.StringArrayToPtr(s_validationLayers),
            PNext = &debugUtilsMessengerCreateInfoEXT,
#else
            EnabledLayerCount = 0,
#endif
        };

        var result = _vk.CreateInstance(&instanceCreateInfo, null, out _instance);

        Marshal.FreeHGlobal((nint)applicationInfo.PApplicationName);
        Marshal.FreeHGlobal((nint)applicationInfo.PEngineName);
        SilkMarshal.Free((nint)instanceCreateInfo.PpEnabledExtensionNames);
#if DEBUG
        SilkMarshal.Free((nint)instanceCreateInfo.PpEnabledLayerNames);
#endif

        if (result != Result.Success)
        {
            Console.Error.WriteLine("Failed to create instance.");
            Environment.Exit(1);
        }

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
    }

    public unsafe void Dispose()
    {
        _debugUtils!.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
        _vk!.DestroyInstance(_instance, null);
        _vk!.Dispose();
    }

    private unsafe string[] GetRequiredExtensions()
    {
        var surfaceExtensions = _surface!.GetRequiredExtensions(out uint surfaceExtensionCount);
        var extensions = SilkMarshal.PtrToStringArray((nint)surfaceExtensions, (int)surfaceExtensionCount);
#if DEBUG
        extensions = [..extensions, ExtDebugUtils.ExtensionName];
#endif
        return extensions;
    }

#if DEBUG
    private unsafe bool AreRequiredValidationLayersSupported()
    {
        uint layerCount = 0;
        _vk!.EnumerateInstanceLayerProperties(&layerCount, null);
        var layers = new LayerProperties[layerCount];
        _vk!.EnumerateInstanceLayerProperties(&layerCount, layers);
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
}

using Euclidium.Core;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Euclidium.Rendering;

public sealed class Shader : IDisposable
{
    private struct ShaderStage(ShaderKind kind, string filename, string code)
    {
        public ShaderKind Kind = kind;
        public string Filename = filename;
        public string Code = code;
        public byte[]? Binary = null;
    }

    private PipelineLayout _pipelineLayout;

    public static Shader? Create(string filepath)
    {
        Shader shader = new(GetShaderStages(filepath));
        // TODO: return null if failed.
        return shader;
    }

    private Shader(ShaderStage[] stages)
    {
        try
        {
            // Get each shader stage binary.
            GetStageBinaries(stages);

            // Create pipeline.
            CreatePipeline(stages);
        }
        catch (Exception e)
        {
            Dispose();
            Console.Error.WriteLine(e);
        }
        finally
        {

        }
    }

    public unsafe void Dispose()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        DisposeHelper.Dispose(ref _pipelineLayout, handle => vk.DestroyPipelineLayout(device, _pipelineLayout, null));
    }

    private static ShaderStage[] GetShaderStages(string filepath)
    {
        var sources = new ShaderStage[s_stageExtensions.Count];
        for (var i = 0; i < s_stageExtensions.Count; ++i)
        {
            (var type, var extension) = s_stageExtensions[i];
            string path = filepath + extension;
            if (Path.Exists(path))
                sources[i] = new(type, Path.GetFileName(path), File.ReadAllText(path));
        }
        return sources.Where(stage => stage.Code != null).ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe static void GetStageBinaries(ShaderStage[] stages)
    {
        using var shaderc = Shaderc.GetApi();
        var compiler = shaderc.CompilerInitialize();
        var options = shaderc.CompileOptionsInitialize();
        shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan13);
        shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);

        var status = CompilationStatus.Success;
        string errorMessage = "No error";
        for (int i = 0; i < stages.Length && status == CompilationStatus.Success; ++i)
        {
            ref var stage = ref stages[i];

            CompilationResult* result = shaderc.CompileIntoSpv(compiler, stage.Code, (nuint)stage.Code.Length, stage.Kind, stage.Filename, "main", options);

            status = shaderc.ResultGetCompilationStatus(result);

            if (status == CompilationStatus.Success)
            {
                byte* bytes = shaderc.ResultGetBytes(result);
                nuint length = shaderc.ResultGetLength(result);
                var binary = new byte[length];
                Marshal.Copy((nint)bytes, binary, 0, (int)length);
                stage.Binary = binary;
            }
            else
                errorMessage = shaderc.ResultGetErrorMessageS(result);

            shaderc.ResultRelease(result);
        }

        shaderc.CompileOptionsRelease(options);
        shaderc.CompilerRelease(compiler);

        if (status != CompilationStatus.Success)
            throw new Exception($"Failed to compile {errorMessage}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreatePipeline(ShaderStage[] stages)
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;
        var extent = context.Extent;

        ShaderModule[]? modules = new ShaderModule[stages.Length];
        var entrypointName = (byte*)SilkMarshal.StringToPtr("main");

        try
        {
            // Create the shader modules and create infos.

            var pipelineShaderStageCreateInfos = new PipelineShaderStageCreateInfo[stages.Length];

            for (int i = 0; i < stages.Length; ++i)
            {
                ref var stage = ref stages[i];

                fixed (byte* binaryPtr = stage.Binary)
                {
                    ShaderModuleCreateInfo shaderModuleCreateInfo = new()
                    {
                        SType = StructureType.ShaderModuleCreateInfo,
                        CodeSize = (nuint)stage.Binary!.Length,
                        PCode = (uint*)binaryPtr,
                    };

                    if (vk.CreateShaderModule(device, &shaderModuleCreateInfo, null, out modules[i]) != Result.Success)
                        throw new Exception("Failed to create shader module.");
                }

                pipelineShaderStageCreateInfos[i] = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = s_stageConversions[stage.Kind],
                    Module = modules[i],
                    PName = entrypointName,
                };
            }

            //var dynamicStates = stackalloc[] { DynamicState.Viewport, DynamicState.Scissor }.ToArray();

            //PipelineDynamicStateCreateInfo pipelineDynamicStateCreateInfo = new()
            //{
            //    SType = StructureType.PipelineDynamicStateCreateInfo,
            //    DynamicStateCount = (uint)dynamicStates.Length,
            //    PDynamicStates = dynamicStatesPtr,
            //};

            PipelineVertexInputStateCreateInfo pipelineVertexInputStateCreateInfo = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = 0, // TODO
                PVertexBindingDescriptions = null, // TODO
                VertexAttributeDescriptionCount = 0, // TODO
                PVertexAttributeDescriptions = null, // TODO
            };

            PipelineInputAssemblyStateCreateInfo pipelineInputAssemblyStateCreateInfo = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList, // TODO
                PrimitiveRestartEnable = false, // TODO
            };

            // TODO
            Viewport viewport = new()
            {
                X = 0,
                Y = 0,
                Width = extent.Width,
                Height = extent.Height,
                MinDepth = 0f,
                MaxDepth = 1f,
            };

            // TODO
            Rect2D scissor = new()
            {
                Offset = { X = 0, Y = 0 },
                Extent = extent,
            };

            PipelineViewportStateCreateInfo pipelineViewportStateCreateInfo = new()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1, // TODO
                PViewports = &viewport, // TODO
                ScissorCount = 1, // TODO
                PScissors = &scissor, // TODO
            };

            PipelineRasterizationStateCreateInfo pipelineRasterizationStateCreateInfo = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = false, // TODO
                RasterizerDiscardEnable = false, // TODO (check shader for "discard;" statement?)
                PolygonMode = PolygonMode.Fill, // TODO (things like wireframe)
                CullMode = CullModeFlags.BackBit, // TODO
                FrontFace = FrontFace.Clockwise, // TODO
                DepthBiasEnable = false, // TODO: 3 other depth bias parameters
                LineWidth = 1f, // TODO
            };

            PipelineMultisampleStateCreateInfo pipelineMultisampleStateCreateInfo = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit, // TODO
                SampleShadingEnable = false, // TODO
                // TODO: 4 other parameters
            };

            PipelineColorBlendAttachmentState pipelineColorBlendAttachmentState = new()
            {
                BlendEnable = false, // TODO
                // TODO: 6 other parameters
                ColorWriteMask =
                    ColorComponentFlags.RBit |
                    ColorComponentFlags.GBit |
                    ColorComponentFlags.BBit |
                    ColorComponentFlags.ABit,
            };

            PipelineColorBlendStateCreateInfo pipelineColorBlendStateCreateInfo = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false, // TODO
                LogicOp = LogicOp.Copy, // TODO
                AttachmentCount = 1, // TODO
                PAttachments = &pipelineColorBlendAttachmentState, // TODO
                // TODO: 1 other parameter
            };

            PipelineLayoutCreateInfo pipelineLayoutCreateInfo = new()
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 0, // TODO
                PSetLayouts = null, // TODO
                PushConstantRangeCount = 0, // TODO
                PPushConstantRanges = null, // TODO
            };

            if (vk.CreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out _pipelineLayout) != Result.Success)
                throw new Exception("Failed to create pipeline layout.");
        }
        finally
        {
            SilkMarshal.Free((nint)entrypointName);
            DisposeHelper.Dispose(ref modules, handle => vk.DestroyShaderModule(device, handle, null));
        }
    }

    // private static helpers

    private static readonly List<(ShaderKind, string)> s_stageExtensions;
    private static readonly Dictionary<ShaderKind, ShaderStageFlags> s_stageConversions;

    static Shader()
    {
        s_stageExtensions =
        [
            (ShaderKind.VertexShader,         ".vert.glsl"),
            (ShaderKind.TessControlShader,    ".tcon.glsl"),
            (ShaderKind.TessEvaluationShader, ".teva.glsl"),
            (ShaderKind.GeometryShader,       ".geom.glsl"),
            (ShaderKind.FragmentShader,       ".frag.glsl"),
            (ShaderKind.ComputeShader,        ".comp.glsl"),
        ];

        s_stageConversions = new()
        {
            [ShaderKind.VertexShader]         = ShaderStageFlags.VertexBit,
            [ShaderKind.TessControlShader]    = ShaderStageFlags.TessellationControlBit,
            [ShaderKind.TessEvaluationShader] = ShaderStageFlags.TessellationEvaluationBit,
            [ShaderKind.GeometryShader]       = ShaderStageFlags.GeometryBit,
            [ShaderKind.FragmentShader]       = ShaderStageFlags.FragmentBit,
            [ShaderKind.ComputeShader]        = ShaderStageFlags.ComputeBit,
        };
    }
}

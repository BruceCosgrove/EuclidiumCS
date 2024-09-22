using Euclidium.Core;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.SPIRV.Reflect;
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
        public byte[]? Binary;
    }

    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;

    private RenderPass? _renderPass;

    public RenderPass RenderPass => _renderPass!;

    public void Create(string filepath)
    {
        var context = Engine.Instance.Window.Context;
        var renderPass = context.RenderPass;

        Create(filepath, renderPass);
    }

    public void Create(string filepath, RenderPass renderPass)
    {
        try
        {
            // Get each shader stage.
            var stages = GetShaderStages(filepath);

            // Get each shader stage binary.
            GetStageBinaries(stages);

            // Get info about each stage.
            GetStageInfo(stages, out var inputDescriptions, out var stride);

            // Create the pipeline.
            CreatePipeline(stages, inputDescriptions, stride, renderPass);
        }
        catch
        {
            Dispose(); // Dispose what was partially created.
            throw;
        }

        _renderPass = renderPass;
    }

    public unsafe void Dispose()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        RenderHelper.Dispose(ref _pipeline, handle => vk.DestroyPipeline(device, _pipeline, null));
        RenderHelper.Dispose(ref _pipelineLayout, handle => vk.DestroyPipelineLayout(device, _pipelineLayout, null));

        _renderPass = null;
    }

    public unsafe void Bind()
    {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var commandBuffer = context.CommandBuffer;
        var extent = context.SwapchainImageExtent;

        vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, _pipeline);

        Viewport viewport = new()
        {
            X = 0,
            Y = 0,
            Width = extent.Width,
            Height = extent.Height,
            MinDepth = 0f,
            MaxDepth = 1f,
        };
        vk.CmdSetViewport(commandBuffer, 0, 1, &viewport);

        Rect2D scissor = new() { Offset = { X = 0, Y = 0 }, Extent = extent };
        vk.CmdSetScissor(commandBuffer, 0, 1, &scissor);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        RenderHelper.Require(status == CompilationStatus.Success, $"Failed to compile {errorMessage}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void GetStageInfo(
        ShaderStage[] stages,
        out VertexInputAttributeDescription[] inputDescriptions,
        out uint stride
    ) {
        using var reflect = Reflect.GetApi();

        ref var stage = ref stages[0];

        ReflectShaderModule module;
        fixed (byte* binary = stage.Binary)
        RenderHelper.Require(
            reflect.CreateShaderModule((nuint)stage.Binary!.Length, binary, &module) == Silk.NET.SPIRV.Reflect.Result.Success
        );

        // For some reason, the spirv reflect inputs are out of order,
        // with location 0 being at the end instead of the beginning.
        var inputs = new nint[module.InputVariableCount];
        Marshal.Copy((nint)module.InputVariables, inputs, 0, (int)module.InputVariableCount);
        inputs = [..inputs.OrderBy(a => (int)((InterfaceVariable*)a)->Location)];

        inputDescriptions = new VertexInputAttributeDescription[module.InputVariableCount];

        uint offset = 0;
        for (uint i = 0; i < inputs.Length; ++i)
        {
            ref var input = ref *(InterfaceVariable*)inputs[i];

            // TODO: I don't think this accounts for matrix location alignment.
            uint componentSize = input.Numeric.Scalar.Width / 8;
            uint componentCount = input.Numeric.Vector.ComponentCount;

            offset += (componentSize - offset) % componentSize; // alignment

            inputDescriptions[i] = new()
            {
                Location = input.Location,
                Binding = 0, // TODO
                // The formats are airectly castable format since all the enum values are the same.
                Format = (Silk.NET.Vulkan.Format)input.Format,
                Offset = offset,
            };

            offset += componentSize * componentCount;
        }

        reflect.DestroyShaderModule(&module);

        stride = offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void CreatePipeline(
        ShaderStage[] stages,
        VertexInputAttributeDescription[] inputDescriptions,
        uint stride,
        RenderPass renderPass
    ) {
        var context = Engine.Instance.Window.Context;
        var vk = context.VK;
        var device = context.Device;

        var pipelineShaderStageCreateInfos = new PipelineShaderStageCreateInfo[stages.Length];

        ShaderModule[]? modules = new ShaderModule[stages.Length];
        var entrypointName = (byte*)SilkMarshal.StringToPtr("main");

        try
        {
            // Create the shader modules and create infos.
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

                    RenderHelper.Require(
                        vk.CreateShaderModule(device, &shaderModuleCreateInfo, null, out modules[i]),
                        "Failed to create shader module."
                    );
                }

                pipelineShaderStageCreateInfos[i] = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = s_stageConversions[stage.Kind],
                    Module = modules[i],
                    PName = entrypointName,
                };
            }

            // TODO
            VertexInputBindingDescription vertexInputBindingDescription = new()
            {
                Binding = 0,
                Stride = stride,
                InputRate = VertexInputRate.Vertex,
            };

            fixed (VertexInputAttributeDescription* inputDescriptionsPtr = inputDescriptions)
            {
                PipelineVertexInputStateCreateInfo pipelineVertexInputStateCreateInfo = new()
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1, // TODO
                    PVertexBindingDescriptions = &vertexInputBindingDescription, // TODO
                    VertexAttributeDescriptionCount = (uint)inputDescriptions.Length,
                    PVertexAttributeDescriptions = inputDescriptionsPtr,
                };

                PipelineInputAssemblyStateCreateInfo pipelineInputAssemblyStateCreateInfo = new()
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList, // TODO
                    PrimitiveRestartEnable = false, // TODO
                };

                PipelineViewportStateCreateInfo pipelineViewportStateCreateInfo = new()
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1, // TODO (e.g. vr support with multiviewport rendering (for each eye))
                    ScissorCount = 1, // TODO (in which case, I imagine the viewport wouldn't resize)
                };

                PipelineRasterizationStateCreateInfo pipelineRasterizationStateCreateInfo = new()
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = false, // TODO
                    RasterizerDiscardEnable = false, // TODO (check shader for "discard;" statement?)
                    PolygonMode = PolygonMode.Fill, // TODO (things like wireframe)
                    CullMode = CullModeFlags.BackBit, // TODO
                    FrontFace = FrontFace.CounterClockwise, // TODO
                    DepthBiasEnable = false, // TODO: 3 other parameters (all for depth bias)
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

                RenderHelper.Require(
                    vk.CreatePipelineLayout(device, &pipelineLayoutCreateInfo, null, out _pipelineLayout),
                    "Failed to create pipeline layout."
                );

                var dynamicStates = stackalloc[] { DynamicState.Viewport, DynamicState.Scissor }.ToArray(); // TODO
                fixed (DynamicState* dynamicStatesPtr = dynamicStates)
                fixed (PipelineShaderStageCreateInfo* pipelineShaderStageCreateInfosPtr = pipelineShaderStageCreateInfos)
                fixed (Pipeline* pipelinePtr = &_pipeline)
                {
                    PipelineDynamicStateCreateInfo pipelineDynamicStateCreateInfo = new()
                    {
                        SType = StructureType.PipelineDynamicStateCreateInfo,
                        DynamicStateCount = (uint)dynamicStates.Length,
                        PDynamicStates = dynamicStatesPtr,
                    };

                    GraphicsPipelineCreateInfo pipelineCreateInfo = new()
                    {
                        SType = StructureType.GraphicsPipelineCreateInfo,
                        StageCount = (uint)stages.Length,
                        PStages = pipelineShaderStageCreateInfosPtr,
                        PVertexInputState = &pipelineVertexInputStateCreateInfo,
                        PInputAssemblyState = &pipelineInputAssemblyStateCreateInfo,
                        PTessellationState = null, // TODO
                        PViewportState = &pipelineViewportStateCreateInfo,
                        PRasterizationState = &pipelineRasterizationStateCreateInfo,
                        PMultisampleState = &pipelineMultisampleStateCreateInfo,
                        PDepthStencilState = null, // TODO
                        PColorBlendState = &pipelineColorBlendStateCreateInfo,
                        PDynamicState = &pipelineDynamicStateCreateInfo,
                        Layout = _pipelineLayout,
                        RenderPass = renderPass.Handle,
                        Subpass = 0, // TODO
                        // TODO: 2 other parameters (for recreating the pipeline)
                    };

                    RenderHelper.Require(
                        vk.CreateGraphicsPipelines(device, default, 1, &pipelineCreateInfo, null, pipelinePtr),
                        "Failed to create pipeline."
                    );
                }
            }
        }
        finally
        {
            SilkMarshal.Free((nint)entrypointName);
            RenderHelper.Dispose(ref modules, handle => vk.DestroyShaderModule(device, handle, null));
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

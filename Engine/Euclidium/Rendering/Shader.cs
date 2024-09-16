using Euclidium.Core;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace Euclidium.Rendering;

public readonly struct ShaderSource(ShaderType type, string code)
{
    public ShaderType Type => type;
    public string Code => code;
}

public class Shader
{
    private readonly uint _id;
    private readonly Dictionary<string, int> _uniformLocations = [];
    private static readonly List<(ShaderType, string)> s_typeExtensions;

    static Shader()
    {
        s_typeExtensions =
        [
            (ShaderType.VertexShader,         ".vert.glsl"),
            (ShaderType.TessControlShader,    ".tcon.glsl"),
            (ShaderType.TessEvaluationShader, ".teva.glsl"),
            (ShaderType.GeometryShader,       ".geom.glsl"),
            (ShaderType.FragmentShader,       ".frag.glsl"),
            (ShaderType.ComputeShader,        ".comp.glsl"),
        ];
    }

    public Shader(List<ShaderSource> sources)
    {
        //_id = CreateProgram(sources);
    }

    public Shader(string filepath)
    {
        List<ShaderSource> sources = new(s_typeExtensions.Count);
        foreach ((var type, var extension) in s_typeExtensions)
        {
            string path = filepath + extension;
            if (Path.Exists(path))
                sources.Add(new ShaderSource(type, File.ReadAllText(path)));
        }
        //_id = CreateProgram(sources);
    }

    public void Destroy()
    {
        //var vk = Engine.Instance.Window.VK;

        //vk.DeleteProgram(_id);
    }

    public void Bind()
    {
        //var vk = Engine.Instance.Window.VK;

        //vk.UseProgram(_id);
    }

    public void SetUniform(string name, Matrix4X4<float> value)
    {
        //var vk = Engine.Instance.Window.VK;

        unsafe
        {
            //vk.UniformMatrix4(GetUniformLocation(name), 1, false, (float*)&value);
        }
    }

    public void SetUniform(string name, float value)
    {
        //var vk = Engine.Instance.Window.VK;

        //vk.Uniform1(GetUniformLocation(name), value);
    }

    //private static uint CreateProgram(List<ShaderSource> sources)
    //{
    //    //var vk = Engine.Instance.Window.VK;

    //    //uint program = vk.CreateProgram();
    //    bool success = true;

    //    List<uint> shaders = sources.ConvertAll(source =>
    //    {
    //        uint shader = CompileShader(source, ref success);
    //        //vk.AttachShader(program, shader);
    //        return shader;
    //    });

    //    if (success)
    //    {
    //        //vk.LinkProgram(program);
    //        //string programInfoLog = vk.GetProgramInfoLog(program);
    //        if (programInfoLog.Length > 0)
    //        {
    //            Console.Error.WriteLine($"Program: {programInfoLog}");
    //            success = false;
    //        }
    //        else
    //        {
    //            //vk.ValidateProgram(program);
    //            //programInfoLog = vk.GetProgramInfoLog(program);
    //            if (programInfoLog.Length > 0)
    //            {
    //                Console.Error.WriteLine($"Program: {programInfoLog}");
    //                success = false;
    //            }
    //        }
    //    }

    //    // Do this regardles of success.
    //    shaders.ForEach(shader =>
    //    {
    //        //vk.DetachShader(program, shader);
    //        //vk.DeleteShader(shader);
    //    });

    //    if (!success)
    //    {
    //        //vk.DeleteProgram(program);
    //        program = 0;
    //    }

    //    return program;
    //}

    //private static uint CompileShader(ShaderSource source, ref bool success)
    //{
    //    //var vk = Engine.Instance.Window.VK;

    //    //uint shader = vk.CreateShader(source.Type);
    //    //vk.ShaderSource(shader, source.Code);

    //    //vk.CompileShader(shader);
    //    //string shaderInfoLog = vk.GetShaderInfoLog(shader);
    //    if (shaderInfoLog.Length > 0)
    //    {
    //        Console.Error.WriteLine($"{source.Type}: {shaderInfoLog}");
    //        //vk.DeleteShader(shader);
    //        shader = 0;
    //        success = false;
    //    }

    //    return shader;
    //}

    //private int GetUniformLocation(string name)
    //{
    //    //var vk = Engine.Instance.Window.VK;

    //    if (!_uniformLocations.TryGetValue(name, out int location))
    //        //_uniformLocations[name] = location = vk.GetUniformLocation(_id, name);
    //    return location;
    //}
}

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
        _id = CreateProgram(sources);
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
        _id = CreateProgram(sources);
    }

    public void Destroy()
    {
        GL gl = Engine.Instance.Window.GL;

        gl.DeleteProgram(_id);
    }

    public void Bind()
    {
        GL gl = Engine.Instance.Window.GL;

        gl.UseProgram(_id);
    }

    public void SetUniform(string name, Matrix4X4<float> value)
    {
        GL gl = Engine.Instance.Window.GL;

        unsafe
        {
            gl.UniformMatrix4(GetUniformLocation(name), 1, false, (float*)&value);
        }
    }

    public void SetUniform(string name, float value)
    {
        GL gl = Engine.Instance.Window.GL;

        gl.Uniform1(GetUniformLocation(name), value);
    }

    private static uint CreateProgram(List<ShaderSource> sources)
    {
        GL gl = Engine.Instance.Window.GL;

        uint program = gl.CreateProgram();
        bool success = true;

        List<uint> shaders = sources.ConvertAll(source =>
        {
            uint shader = CompileShader(source, ref success);
            gl.AttachShader(program, shader);
            return shader;
        });

        if (success)
        {
            gl.LinkProgram(program);
            string programInfoLog = gl.GetProgramInfoLog(program);
            if (programInfoLog.Length > 0)
            {
                Console.Error.WriteLine($"Program: {programInfoLog}");
                success = false;
            }
            else
            {
                gl.ValidateProgram(program);
                programInfoLog = gl.GetProgramInfoLog(program);
                if (programInfoLog.Length > 0)
                {
                    Console.Error.WriteLine($"Program: {programInfoLog}");
                    success = false;
                }
            }
        }

        // Do this regardles of success.
        shaders.ForEach(shader =>
        {
            gl.DetachShader(program, shader);
            gl.DeleteShader(shader);
        });

        if (!success)
        {
            gl.DeleteProgram(program);
            program = 0;
        }

        return program;
    }

    private static uint CompileShader(ShaderSource source, ref bool success)
    {
        GL gl = Engine.Instance.Window.GL;

        uint shader = gl.CreateShader(source.Type);
        gl.ShaderSource(shader, source.Code);

        gl.CompileShader(shader);
        string shaderInfoLog = gl.GetShaderInfoLog(shader);
        if (shaderInfoLog.Length > 0)
        {
            Console.Error.WriteLine($"{source.Type}: {shaderInfoLog}");
            gl.DeleteShader(shader);
            shader = 0;
            success = false;
        }

        return shader;
    }

    private int GetUniformLocation(string name)
    {
        GL gl = Engine.Instance.Window.GL;

        if (!_uniformLocations.TryGetValue(name, out int location))
            _uniformLocations[name] = location = gl.GetUniformLocation(_id, name);
        return location;
    }
}

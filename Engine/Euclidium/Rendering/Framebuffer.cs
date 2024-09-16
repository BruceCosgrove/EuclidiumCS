using Euclidium.Core;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System.Runtime.InteropServices;

namespace Euclidium.Rendering;

// Channels
//  R = Red
//  G = Green
//  B = Blue
//  A = Alpha
//  D = Depth
//  S = stencil
// Types:
//  i = int
//  u = unsigned int
//  f = float
public enum FramebufferFormat
{
    None = 0,

    // Color formats
    Ru8_Gu8_Bu8_Au8,
    Ru32,

    // Depth and stencil formats
    Du24_Su8,
}

public struct FramebufferAttachmentInfo(
    FramebufferFormat format = FramebufferFormat.None,
    TextureMinFilter minFilter = TextureMinFilter.Nearest,
    TextureMagFilter magFilter = TextureMagFilter.Nearest,
    TextureWrapMode horizontalWrapMode = TextureWrapMode.ClampToEdge,
    TextureWrapMode verticalWrapMode = TextureWrapMode.ClampToEdge
) {
    public FramebufferFormat Format = format;
    public TextureMinFilter MinFilter = minFilter;
    public TextureMagFilter MagFilter = magFilter;
    public TextureWrapMode HorizontalWrapMode = horizontalWrapMode;
    public TextureWrapMode VerticalWrapMode = verticalWrapMode;
}

public struct FramebufferInfo(
    uint width,
    uint height,
    List<FramebufferAttachmentInfo> colorAttachments,
    FramebufferAttachmentInfo depthAttachment = new()
) {
    public uint Width = width;
    public uint Height = height;
    public List<FramebufferAttachmentInfo> ColorAttachments = colorAttachments;
    public FramebufferAttachmentInfo DepthAttachment = depthAttachment;
}

public class Framebuffer
{
    private FramebufferInfo _info;
    private uint _framebufferID;
    private List<uint> _colorAttachmentIDs;
    private uint _depthAttachmentID;

    public static bool Create(FramebufferInfo info, out Framebuffer? framebuffer)
    {
        framebuffer = null;

        // Can't create a framebuffer with no attachments.
        if (info.ColorAttachments.Count == 0 && info.DepthAttachment.Format == FramebufferFormat.None)
            return false;

        var vk = Engine.Instance.Window.VK;

        // Create the framebuffer itself.
        //uint framebufferID = vk.CreateFramebuffer();
        //vk.BindFramebuffer(FramebufferTarget.Framebuffer, framebufferID);

        // Create its color attachments.
        List<uint> colorAttachmentIDs = new(new uint[info.ColorAttachments.Count]);
        //vk.CreateTextures(TextureTarget.Texture2D, CollectionsMarshal.AsSpan(colorAttachmentIDs));
        for (int i = 0; i < info.ColorAttachments.Count; ++i)
        {
            var colorAttachmentInfo = info.ColorAttachments[i];
            var colorAttachmentID = colorAttachmentIDs[i];
            var (format, internalFormat, type) = s_colorFormats[colorAttachmentInfo.Format];

            //vk.BindTexture(TextureTarget.Texture2D, colorAttachmentID);
            //vk.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, info.Width, info.Height, 0, format, type, ReadOnlySpan<byte>.Empty);
            //vk.TextureParameter(colorAttachmentID, GLEnum.TextureMinFilter, (int)colorAttachmentInfo.MinFilter);
            //vk.TextureParameter(colorAttachmentID, GLEnum.TextureMagFilter, (int)colorAttachmentInfo.MagFilter);
            //vk.TextureParameter(colorAttachmentID, GLEnum.TextureWrapS, (int)colorAttachmentInfo.HorizontalWrapMode);
            //vk.TextureParameter(colorAttachmentID, GLEnum.TextureWrapT, (int)colorAttachmentInfo.VerticalWrapMode);
            //vk.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, TextureTarget.Texture2D, colorAttachmentID, 0);
        }

        // Create its depth attachment.
        uint depthAttachmentID = 0;
        if (info.DepthAttachment.Format != FramebufferFormat.None)
        {
            var depthAttachmentInfo = info.DepthAttachment;
            var (internalFormat, kind) = s_depthFormats[depthAttachmentInfo.Format];

            //depthAttachmentID = vk.CreateTexture(TextureTarget.Texture2D);
            //vk.BindTexture(TextureTarget.Texture2D, depthAttachmentID);
            //vk.TextureStorage2D(depthAttachmentID, 1, (GLEnum)internalFormat, info.Width, info.Height);
            //vk.TextureParameter(depthAttachmentID, GLEnum.TextureMinFilter, (int)depthAttachmentInfo.MinFilter);
            //vk.TextureParameter(depthAttachmentID, GLEnum.TextureMagFilter, (int)depthAttachmentInfo.MagFilter);
            //vk.TextureParameter(depthAttachmentID, GLEnum.TextureWrapS, (int)depthAttachmentInfo.HorizontalWrapMode);
            //vk.TextureParameter(depthAttachmentID, GLEnum.TextureWrapT, (int)depthAttachmentInfo.VerticalWrapMode);
            //vk.FramebufferTexture2D(FramebufferTarget.Framebuffer, kind, TextureTarget.Texture2D, depthAttachmentID, 0);
        }

        // Set the draw buffers appropriately (only color attachments matter).
        if (info.ColorAttachments.Count > 0)
        {
            var buffers = Enumerable.Range(0, info.ColorAttachments.Count).ToList()
                .ConvertAll(i => DrawBufferMode.ColorAttachment0 + i);
            //vk.DrawBuffers(CollectionsMarshal.AsSpan(buffers));
        }
        else
        {
            //vk.DrawBuffer(DrawBufferMode.None);
        }

        //vk.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        //Framebuffer result = new(info, framebufferID, colorAttachmentIDs, depthAttachmentID);
        //if (vk.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        //{
        //    Console.Error.WriteLine("Framebuffer incomplete.");
        //    result.Destroy();
        //    return false;
        //}

        //framebuffer = result;
        return true;
    }

    public void OnViewportResize(Vector2D<int> size)
    {
        _info.Width = (uint)size.X;
        _info.Height = (uint)size.Y;
        if (Create(_info, out var newFramebuffer))
        {
            Destroy();
            _framebufferID = newFramebuffer!._framebufferID;
            _colorAttachmentIDs = newFramebuffer!._colorAttachmentIDs;
            _depthAttachmentID = newFramebuffer!._depthAttachmentID;
        }
    }

    public void Destroy()
    {
        var vk = Engine.Instance.Window.VK;

        //vk.DeleteTexture(_depthAttachmentID);
        //vk.DeleteTextures(CollectionsMarshal.AsSpan(_colorAttachmentIDs));
        //vk.DeleteFramebuffer(_framebufferID);
    }

    public void Bind()
    {
        var vk = Engine.Instance.Window.VK;

        //vk.BindFramebuffer(FramebufferTarget.Framebuffer, _framebufferID);
        //vk.Viewport(0, 0, _info.Width, _info.Height);
    }

    public void Unbind()
    {
        var vk = Engine.Instance.Window.VK;

        //vk.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public uint GetColorAttachment(int index = 0) => _colorAttachmentIDs[index];

    public uint GetDepthAttachment() => _depthAttachmentID;

    private static readonly Dictionary<FramebufferFormat, (PixelFormat, InternalFormat, PixelType)> s_colorFormats;
    private static readonly Dictionary<FramebufferFormat, (InternalFormat, FramebufferAttachment)> s_depthFormats;

    static Framebuffer()
    {
        s_colorFormats = new()
        {
            [FramebufferFormat.Ru8_Gu8_Bu8_Au8] = (PixelFormat.Rgba, InternalFormat.Rgba8, PixelType.UnsignedByte),
            [FramebufferFormat.Ru32] = (PixelFormat.RedInteger, InternalFormat.R32ui, PixelType.UnsignedInt),
        };

        s_depthFormats = new()
        {
            [FramebufferFormat.Du24_Su8] = (InternalFormat.Depth24Stencil8, FramebufferAttachment.DepthStencilAttachment),
        };
    }

    private Framebuffer(FramebufferInfo info, uint framebufferID, List<uint> colorAttachmentIDs, uint depthAttachmentID)
    {
        _info = info;
        _framebufferID = framebufferID;
        _colorAttachmentIDs = colorAttachmentIDs;
        _depthAttachmentID = depthAttachmentID;
    }
}

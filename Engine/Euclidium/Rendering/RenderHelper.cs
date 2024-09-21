using Silk.NET.Vulkan;

namespace Euclidium.Rendering;

/// <summary>
/// Provides internal utility functions for rendering.
/// </summary>
internal static class RenderHelper
{
    /// <summary>
    /// Destroys an array of render handles using the provided destroy function, if they exist.
    /// </summary>
    public static void Dispose<T>(ref T[]? handles, Action<T> destroyFunc) where T : struct
    {
        if (handles != null)
        {
            for (uint i = 0; i < handles.Length; ++i)
                Dispose(ref handles[i], destroyFunc);
            handles = null;
        }
    }

    /// <summary>
    /// Destroys a render handle using the provided destroy function, if it exists.
    /// </summary>
    public static void Dispose<T>(ref T handle, Action<T> destroyFunc) where T : struct
    {
        if (!handle.Equals(default(T)))
        {
            destroyFunc(handle);
            handle = default;
        }
    }

    /// <summary>
    /// Disposes a render API or API extension, if it exists.
    /// </summary>
    public static void Dispose<T>(ref T? disposable) where T : class, IDisposable
    {
        if (disposable != null)
        {
            disposable.Dispose();
            disposable = null;
        }
    }

    /// <summary>
    /// Throws an exception with the given error message if the result is not success.
    /// </summary>
    public static void Require(Result result, string? errorMessage = null) =>
        Require(result == Result.Success, errorMessage);

    /// <summary>
    /// Throws an exception with the given error message if the result is not success.
    /// </summary>
    public static void Require(bool result, string? errorMessage = null)
    {
        if (!result)
            throw new Exception(errorMessage);
    }
}

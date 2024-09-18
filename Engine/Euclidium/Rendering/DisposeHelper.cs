namespace Euclidium.Rendering;

/// <summary>
/// Provides utility functions for disposing render handles.
/// </summary>
internal static class DisposeHelper
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
}

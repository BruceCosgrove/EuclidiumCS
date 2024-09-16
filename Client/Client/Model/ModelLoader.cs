using Euclidium.Core;
using NativeFileDialogSharp;

namespace Client.Model;

public static class ModelLoader
{
    // TODO: Replace this on the fly thread creation with a form of thread pool.
    // Right now, this is relying on the synchronization point of the thread closing
    // to send the finished flag, but that needs to be explicit with a thread pool.

    public static void Open(LoadableModel loadable)
    {
        new Thread(() =>
        {
            string defaultPath = Path.GetFullPath("./Resources/Models/");
            DialogResult result = Dialog.FileOpen("ob4", defaultPath);
            if (result.IsOk)
                Load(result.Path, loadable);
        }).Start();
    }

    public static void Load(string path, LoadableModel loadable)
    {
        if (path != null)
        {
            if (Engine.Instance.IsMainThread)
                new Thread(() => LoadImpl(path, loadable)).Start();
            else
                LoadImpl(path, loadable);
        }
    }

    private static void LoadImpl(string path, LoadableModel loadable)
    {
        if (Model4D.Load(path, out Model4D? model))
            loadable.Store(model!, path);
    }
}

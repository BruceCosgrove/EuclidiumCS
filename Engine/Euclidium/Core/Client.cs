namespace Euclidium.Core;

public abstract class Client
{
    protected static void Start(string[] args, Client client) =>
        Engine.Start(args, client);

    protected internal abstract void InitializeCallbacks();
}

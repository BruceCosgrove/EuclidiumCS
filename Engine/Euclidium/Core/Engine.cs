namespace Euclidium.Core;

public sealed partial class Engine
{
    public static Engine Instance => _instance!;
    private static Engine? _instance;
    private static Client? _client; // Keep alive.

    public bool IsMainThread => _mainThreadID == Environment.CurrentManagedThreadId;
    private readonly int _mainThreadID;

    public Window Window => _window;
    private readonly Window _window;

    private Engine()
    {
        _mainThreadID = Environment.CurrentManagedThreadId;
        _window = new();
    }

    // Used by Client to start the engine.
    internal static void Start(string[] args, Client client)
    {
        if (_instance == null)
        {
            _instance = new();
            _client = client;
            _client.InitializeCallbacks();
            _instance._window.Run();
        }
    }
}

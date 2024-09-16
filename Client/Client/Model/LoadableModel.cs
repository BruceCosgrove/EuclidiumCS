namespace Client.Model;

public class LoadableModel
{
    private Model4D? _model;
    private string? _path;

    public bool IsLoaded => _model != null;

    public void Consume(out Model4D model, out string path)
    {
        model = _model!;
        path = _path!;
        _model = null;
        _path = null;
    }

    public void Store(Model4D model, string path)
    {
        _model = model;
        _path = path;
    }
}

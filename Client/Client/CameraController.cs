using Client.Model;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Client;

public class CameraController(float fovRadians, float nearPlane, float farPlane)
{
    // Current projection matrix.
    private Matrix4X4<float> _projectionMatrix;
    // Current view matrix.
    private Matrix4X4<float> _viewMatrix;
    // Current 4D rotation matrix.
    private Matrix4X4<float> _rotation4DMatrix;
    // Current direction matrix.
    // Row 1 is the +X/right direction.
    // Row 2 is the +Y/up direction.
    // Row 3 is the -Z/forward direction.
    // Row 4 is the +W/inner direction.
    private Matrix4X4<float> _directionMatrix;

    // Set this to recalculate the projection matrix upon its next get.
    private bool _recalculateProjectionMatrix = true;
    // Set this to recalculate the view matrix upon its next get.
    private bool _recalculateViewMatrix = true;
    // Set this to recalculate the 4D rotation matrix upon its next get.
    private bool _recalculateRotation4DMatrix = true;
    // Set this to recalculate the forward, right, and up directions on their next get.
    private bool _recalculateDirectionMatrix = true;

    // Current field of view, in radians.
    private float _fovRadians = fovRadians;
    // Current near clipping plane.
    private float _nearPlane = nearPlane;
    // Current far clipping plane.
    private float _farPlane = farPlane;
    // Current aspect ratio.
    private float _aspect;

    // Current 4D position.
    private Vector4D<float> _position;
    // Current 3D (YZ, XZ, and XY) rotations.
    private Vector3D<float> _rotation;
    // Current 4D (XW, YW, and ZW) rotations.
    private Vector3D<float> _rotation4D;

    // Current movement direction, as indicated by user input.
    private Vector4D<int> _movementDirection;
    // Previous mouse position.
    private Vector2D<int> _lastMousePosition;
    // The current view area size (likely window size).
    private Vector2D<int> _viewAreaSize;
    // If the camera is sprinting.
    private bool _sprinting;
    // If the camera is enabled.
    private bool _enabled;
    // If the mouse inputs should be used to rotate in 4D instead of 3D.
    private bool _rotate4D;

    // Current movement speed, in meters / second.
    public float MovementSpeed = 1.0f;
    // Current mouse sensitivity.
    public float Sensitivity = 1.5f;
    // Current sprint multiplier.
    // This is multiplied by MovementSpeed while the sprint key is held down.
    public float SprintMultiplier = 2.0f;

    public Matrix4X4<float> ViewProjectionMatrix => ViewMatrix * ProjectionMatrix;
    public Vector4D<float> Right => DirectionMatrix.Row1;
    public Vector4D<float> Up => DirectionMatrix.Row2;
    public Vector4D<float> Forward => DirectionMatrix.Row3;
    public Vector4D<float> Inner => DirectionMatrix.Row4;

    public Matrix4X4<float> ProjectionMatrix { get { RecalculateProjectionMatrixIfNecessary(); return _projectionMatrix; } }
    public Matrix4X4<float> ViewMatrix { get { RecalculateViewMatrixIfNecessary(); return _viewMatrix; } }
    public Matrix4X4<float> Rotation4DMatrix { get { RecalculateRotation4DMatrixIfNecessary(); return _rotation4DMatrix; } }
    public Matrix4X4<float> DirectionMatrix { get { RecalculateDirectionMatrixIfNecessary(); return _directionMatrix; } }

    public float FOVRadians { get => _fovRadians; set => SetProjectionMatrixField(ref _fovRadians, value); }
    public float NearPlane { get => _nearPlane; set => SetProjectionMatrixField(ref _nearPlane, value); }
    public float FarPlane { get => _farPlane; set => SetProjectionMatrixField(ref _farPlane, value); }

    public Vector4D<float> Position { get => _position; set => SetViewMatrixField(ref _position, value); }
    public Vector3D<float> Rotation { get => _rotation; set => SetViewMatrixField(ref _rotation, value); }
    public Vector3D<float> Rotation4D { get => _rotation4D; set => SetRotation4DMatrixField(ref _rotation4D, value); }

    public bool Enabled { get => _enabled; set => _enabled = value; }

    public void OnUpdate(double deltaTime)
    {
        if (_enabled && _movementDirection != Vector4D<int>.Zero)
        {
            float movementAmount = MovementSpeed * (float)deltaTime;
            if (_sprinting)
                movementAmount *= SprintMultiplier;
            var movement = Vector4D.Normalize(_movementDirection.As<float>()) * movementAmount;
            Position += movement * DirectionMatrix;
        }
    }

    public void OnViewportResize(Vector2D<int> size)
    {
        _viewAreaSize = size;
        _aspect = (float)size.X / size.Y;
        _recalculateProjectionMatrix = true;
    }

    public void OnKeyChange(Key key, bool pressed)
    {
        int direction = pressed ? 1 : -1;
        switch (key)
        {
            case Key.A: // Left
                _movementDirection.X -= direction;
                break;
            case Key.D: // Right
                _movementDirection.X += direction;
                break;
            case Key.ControlLeft: // Down
                _movementDirection.Y -= direction;
                break;
            case Key.Space: // Up
                _movementDirection.Y += direction;
                break;
            case Key.W: // Forward
                _movementDirection.Z -= direction;
                break;
            case Key.S: // Backward
                _movementDirection.Z += direction;
                break;
            case Key.Q: // Outer
                _movementDirection.W -= direction;
                break;
            case Key.E: // Inner
                _movementDirection.W += direction;
                break;
            case Key.ShiftLeft:
                _sprinting = pressed;
                break;
            //case Key.AltLeft:
            //    _enabled = pressed;
            //    break;
        }
    }

    public void OnMouseMove(Vector2D<int> position)
    {
        var mouseMovement = Vector2D<int>.Zero;
        if (_lastMousePosition != Vector2D<int>.Zero)
            mouseMovement = position - _lastMousePosition;
        _lastMousePosition = position;

        if (_enabled)
        {
            float rotationDX = -mouseMovement.X * Sensitivity / _viewAreaSize.X;
            float rotationDY = -mouseMovement.Y * Sensitivity / _viewAreaSize.Y;
            if (_rotate4D)
                Rotation4D += new Vector3D<float>(rotationDX, 0f, rotationDY);
            else
                Rotation += new Vector3D<float>(rotationDY, rotationDX, 0f);
            _recalculateDirectionMatrix = true;
        }
    }

    public void OnMouseButtonChange(MouseButton button, bool pressed)
    {
        if (button == MouseButton.Left)
            _rotate4D = pressed;
    }

    private void RecalculateProjectionMatrixIfNecessary()
    {
        if (_recalculateProjectionMatrix)
        {
            _recalculateProjectionMatrix = false;

            _projectionMatrix = Matrix4X4.CreatePerspectiveFieldOfView(_fovRadians, _aspect, _nearPlane, _farPlane);
        }
    }

    private void RecalculateViewMatrixIfNecessary()
    {
        if (_recalculateViewMatrix)
        {
            _recalculateViewMatrix = false;

            var translation = Matrix4X4.CreateTranslation(new Vector3D<float>(_position.X, _position.Y, _position.Z));
            var rotation = Matrix4X4.CreateRotationX(_rotation.X) * Matrix4X4.CreateRotationY(_rotation.Y);
            Matrix4X4.Invert(rotation * translation, out _viewMatrix);
        }
    }

    private void RecalculateRotation4DMatrixIfNecessary()
    {
        if (_recalculateRotation4DMatrix)
        {
            _recalculateRotation4DMatrix = false;

            var rotationXW = Math4D.CreateRotationXW(_rotation4D.X);
            var rotationYW = Math4D.CreateRotationYW(_rotation4D.Y);
            var rotationZW = Math4D.CreateRotationZW(_rotation4D.Z);
            _rotation4DMatrix = rotationXW * rotationYW * rotationZW;
        }
    }

    private void RecalculateDirectionMatrixIfNecessary()
    {
        if (_recalculateDirectionMatrix)
        {
            _recalculateDirectionMatrix = false;

            // TODO: is this rotation 4d correct here?
            var rotation = Rotation4DMatrix * Matrix4X4.CreateRotationY(_rotation.Y);

            _directionMatrix.Row1 = Vector4D.Transform(new Vector4D<float>(1f, 0f, 0f, 0f), rotation);
            _directionMatrix.Row2 = Vector4D.Transform(new Vector4D<float>(0f, 1f, 0f, 0f), rotation);
            _directionMatrix.Row3 = Vector4D.Transform(new Vector4D<float>(0f, 0f, 1f, 0f), rotation);
            _directionMatrix.Row4 = Vector4D.Transform(new Vector4D<float>(0f, 0f, 0f, 1f), rotation);
        }
    }

    // Sets the given field to the given value and
    // flags the projection matrix to be recalculated.
    private void SetProjectionMatrixField<T>(ref T field, T value) where T : struct
    {
        field = value;
        _recalculateProjectionMatrix = true;
    }

    // Sets the given field to the given value and
    // flags the view matrix to be recalculated.
    private void SetViewMatrixField<T>(ref T field, T value) where T : struct
    {
        field = value;
        _recalculateViewMatrix = true;
    }

    // Sets the given field to the given value and
    // flags the rotation 4D matrix to be recalculated.
    private void SetRotation4DMatrixField<T>(ref T field, T value) where T : struct
    {
        field = value;
        _recalculateRotation4DMatrix = true;
    }
}

using Silk.NET.Maths;

namespace Client.Model;

public static class Math4D
{
    public static Matrix4X4<T> CreateRotationXW<T>(T radians) where T : unmanaged, IFormattable, IEquatable<T>, IComparable<T>
    {
        Matrix4X4<T> identity = Matrix4X4<T>.Identity;
        T val = Scalar.Cos(radians);
        T val2 = Scalar.Sin(radians);
        identity.M11 = val;
        identity.M14 = Scalar.Negate(val2);
        identity.M41 = val2;
        identity.M44 = val;
        return identity;
    }

    public static Matrix4X4<T> CreateRotationYW<T>(T radians) where T : unmanaged, IFormattable, IEquatable<T>, IComparable<T>
    {
        Matrix4X4<T> identity = Matrix4X4<T>.Identity;
        T val = Scalar.Cos(radians);
        T val2 = Scalar.Sin(radians);
        identity.M22 = val;
        identity.M24 = Scalar.Negate(val2);
        identity.M42 = val2;
        identity.M44 = val;
        return identity;
    }

    public static Matrix4X4<T> CreateRotationZW<T>(T radians) where T : unmanaged, IFormattable, IEquatable<T>, IComparable<T>
    {
        Matrix4X4<T> identity = Matrix4X4<T>.Identity;
        T val = Scalar.Cos(radians);
        T val2 = Scalar.Sin(radians);
        identity.M33 = val;
        identity.M34 = Scalar.Negate(val2);
        identity.M43 = val2;
        identity.M44 = val;
        return identity;
    }
}

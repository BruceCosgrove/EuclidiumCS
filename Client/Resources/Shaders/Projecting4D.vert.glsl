#version 460 core

layout (location = 0) in vec4 iPosition;
layout (location = 1) in vec4 iColor;

layout (location = 0) out vec4 oColor;

layout (push_constant) uniform Constants
{
    mat4 uViewProjection;
    mat4 uRotation4D;
};

void main()
{
    oColor = iColor;
    gl_Position = uViewProjection * vec4((uRotation4D * iPosition).xyz, 1);
}

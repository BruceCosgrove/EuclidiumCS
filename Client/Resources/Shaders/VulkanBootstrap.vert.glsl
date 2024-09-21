#version 460 core

layout (location = 0) in vec2 iPosition;
layout (location = 1) in vec3 iColor;

layout (location = 0) out vec3 oColor;

void main()
{
    oColor = iColor;
    gl_Position = vec4(iPosition, 0, 1);
}

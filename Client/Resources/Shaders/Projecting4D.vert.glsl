#version 460 core

layout (location = 0) in vec4 iPosition;
layout (location = 1) in vec4 iColor;

layout (location = 0) out vec4 oColor;

uniform mat4 uViewProjection;
uniform mat4 uRotation4D;

void main()
{
	oColor = iColor;
	gl_Position = uViewProjection * vec4((uRotation4D * iPosition).xyz, 1);
}

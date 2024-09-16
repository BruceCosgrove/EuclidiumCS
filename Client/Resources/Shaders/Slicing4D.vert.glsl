#version 460 core

layout (location = 0) in vec4 iPosition;
layout (location = 1) in vec4 iColor;

layout (location = 0) out vec4 oPosition;
layout (location = 1) out vec4 oColor;
layout (location = 2) out uint oHyperplaneSide;

uniform mat4 uRotation4D;
uniform float uPositionW;

void main()
{
	oPosition = uRotation4D * iPosition - vec4(0, 0, 0, uPositionW);
	oColor = iColor;
	// Which side of the hyperplane each vertex is on.
	// 0 => negative side, 1 => positive side.
	oHyperplaneSide = floatBitsToUint(oPosition.w) >> 31; // Extract sign bit
}

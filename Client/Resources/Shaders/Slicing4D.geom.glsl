#version 460 core

#define VERTEX_COUNT 4
#define SEGMENT_COUNT 6

layout (lines_adjacency) in;
layout (triangle_strip, max_vertices = 4) out;

layout (location = 0) in vec4 iPositions[VERTEX_COUNT];
layout (location = 1) in vec4 iColors[VERTEX_COUNT];
layout (location = 2) in uint iHyperplaneSides[VERTEX_COUNT];

layout (location = 0) out vec4 oColor;

uniform mat4 uViewProjection;

const uvec2[SEGMENT_COUNT] LineSegmentIndices = uvec2[](
	uvec2(0, 1), // A->B
	uvec2(0, 2), // A->C
	uvec2(0, 3), // A->D
	uvec2(1, 2), // B->C
	uvec2(1, 3), // B->D
	uvec2(2, 3)  // C->D
);

// Values:
// X: 0..5 => Line segment index.
// Y: 0, 1 => Discarded, Included.
// Indexing:
// Bits [1:0] => Vertex Index
// Bits [2:2] => Hyperplane side of A
// Bits [3:3] => Hyperplane side of B
// Bits [4:4] => Hyperplane side of C
// Bits [5:5] => Hyperplane side of D
// LUT idea from CodeParade: https://youtu.be/dbq9uX_MycY?si=4xunJmr8v898fVXp&t=381
const uvec2[1 << VERTEX_COUNT][VERTEX_COUNT] CombinationLUT = uvec2[][](
	uvec2[](uvec2(0, 0), uvec2(0, 0), uvec2(0, 0), uvec2(0, 0)), // ----
	uvec2[](uvec2(0, 1), uvec2(1, 1), uvec2(2, 1), uvec2(0, 0)), // +---
	uvec2[](uvec2(0, 1), uvec2(4, 1), uvec2(3, 1), uvec2(0, 0)), // -+--
	uvec2[](uvec2(1, 1), uvec2(2, 1), uvec2(3, 1), uvec2(4, 1)), // ++--
	uvec2[](uvec2(1, 1), uvec2(3, 1), uvec2(5, 1), uvec2(0, 0)), // --+-
	uvec2[](uvec2(2, 1), uvec2(0, 1), uvec2(5, 1), uvec2(3, 1)), // +-+-
	uvec2[](uvec2(0, 1), uvec2(4, 1), uvec2(1, 1), uvec2(5, 1)), // -++-
	uvec2[](uvec2(2, 1), uvec2(4, 1), uvec2(5, 1), uvec2(0, 0)), // +++-
	uvec2[](uvec2(2, 1), uvec2(5, 1), uvec2(4, 1), uvec2(0, 0)), // ---+
	uvec2[](uvec2(0, 1), uvec2(1, 1), uvec2(4, 1), uvec2(5, 1)), // +--+
	uvec2[](uvec2(0, 1), uvec2(3, 1), uvec2(2, 1), uvec2(5, 1)), // -+-+
	uvec2[](uvec2(1, 1), uvec2(5, 1), uvec2(3, 1), uvec2(0, 0)), // ++-+
	uvec2[](uvec2(1, 1), uvec2(3, 1), uvec2(2, 1), uvec2(4, 1)), // --++
	uvec2[](uvec2(0, 1), uvec2(3, 1), uvec2(4, 1), uvec2(0, 0)), // +-++
	uvec2[](uvec2(0, 1), uvec2(2, 1), uvec2(1, 1), uvec2(0, 0)), // -+++
	uvec2[](uvec2(0, 0), uvec2(0, 0), uvec2(0, 0), uvec2(0, 0))  // ++++
);

void main()
{
	// Reject the tetrahedron if all vertices are on the same side of the hyperplane.
	// In a scene with many tetrahedra, this will be a significant proportion.
	if (
		((iHyperplaneSides[0] & iHyperplaneSides[1] & iHyperplaneSides[2] & iHyperplaneSides[3]) == 1) ||
		((iHyperplaneSides[0] | iHyperplaneSides[1] | iHyperplaneSides[2] | iHyperplaneSides[3]) == 0)
	)
		return;

	// Calculate potential intersections with each line segment.
	// NOTE: intersections here is a vec4 only to cache the t-value to use later.
	vec4[SEGMENT_COUNT] intersections;
	for (uint i = 0; i < SEGMENT_COUNT; ++i)
	{
		const uvec2 indices = LineSegmentIndices[i];
		// If the endpoints are on opposite sides of the hyperplane, there's an intersection.
		if (iHyperplaneSides[indices.x] != iHyperplaneSides[indices.y])
		{
			// Get the endpoints of the line segment.
			const vec4 a = iPositions[indices.x];
			const vec4 b = iPositions[indices.y];
			// Calculate the intersection point.
			const float t = a.w / (a.w - b.w);
			const vec3 intersection = (1.0 - t) * a.xyz + t * b.xyz;
			intersections[i] = vec4(intersection, t);
		}
	}

	const uint hyperplaneIndex =
		(iHyperplaneSides[0] << 0) |
		(iHyperplaneSides[1] << 1) |
		(iHyperplaneSides[2] << 2) |
		(iHyperplaneSides[3] << 3) ;
	const uvec2[] combinations = CombinationLUT[hyperplaneIndex];
	for (uint i = 0; i < VERTEX_COUNT; ++i)
	{
		const uvec2 combination = combinations[i];
		// If this vertex should be included.
		if (combination.y != 0)
		{
			const uvec2 indices = LineSegmentIndices[combination.x];
			const vec4 intersection = intersections[combination.x];
			//if (gl_PrimitiveIDIn == 0)
				oColor = mix(iColors[indices.x], iColors[indices.y], intersection.w);
			//else
			//	oColor = vec4(vec3(gl_PrimitiveIDIn / 4.0), 1);
			gl_Position = uViewProjection * vec4(intersection.xyz, 1.0);
			EmitVertex();
		}
	}
}

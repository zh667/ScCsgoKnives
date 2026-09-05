#version 300 es
// First-person knife, PBR pass. Geometry arrives already in view space (the
// renderer composes placement * body motion into the world matrix and uses an
// identity view), so "worldView" here is that view-space matrix.
// <Semantic Name='POSITION' Attribute='a_position' />
// <Semantic Name='NORMAL' Attribute='a_normal' />
// <Semantic Name='TEXCOORD' Attribute='a_texcoord' />

uniform mat4 u_worldViewProjectionMatrix;
uniform mat4 u_worldViewMatrix;

in vec3 a_position;
in vec3 a_normal;
in vec2 a_texcoord;

out vec3 v_viewPos;
out vec3 v_viewNormal;
out vec2 v_texcoord;

void main()
{
	vec4 viewPos = u_worldViewMatrix * vec4(a_position, 1.0);
	v_viewPos = viewPos.xyz;
	// Uniform scale + rotation only, so the 3x3 is a valid normal transform.
	v_viewNormal = normalize(mat3(u_worldViewMatrix) * a_normal);
	v_texcoord = a_texcoord;
	gl_Position = u_worldViewProjectionMatrix * vec4(a_position, 1.0);
	OPENGL_POSITION_FIX;
}
